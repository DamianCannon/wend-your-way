using System.Globalization;
using System.IO.Compression;
using Microsoft.VisualBasic.FileIO;
using Npgsql;

namespace WendYourWay.Data.Outings;

/// <summary>What importing a Strava archive did.</summary>
/// <param name="Imported">Outings added, by kind ("run", "walk", "hike", "ride", "gravel_ride").</param>
/// <param name="AlreadyThere">Activities already imported, from this archive or their own files.</param>
/// <param name="SkippedByType">Activities with no way on the ground (swims, rides on a trainer), by Strava's type.</param>
/// <param name="NotRead">Activities whose file couldn't be imported, with why.</param>
/// <param name="OutsideEnglandAndWales">Activities that never entered England or Wales (only counted when the Network is clipped to them).</param>
/// <param name="Deleted">Activities the user deleted as Outings (T106), not imported again.</param>
public sealed record StravaImport(
    IReadOnlyDictionary<string, int> ImportedByKind, int AlreadyThere, IReadOnlyDictionary<string, int> SkippedByType,
    IReadOnlyList<(string Activity, string Why)> NotRead, int OutsideEnglandAndWales, int Deleted = 0)
{
    public int Imported => ImportedByKind.Values.Sum();
}

/// <summary>
/// Imports the user's history from Strava's bulk export, read in place from its zip: activities.csv
/// for each activity's type, name and file, and the activity files themselves (GPX, gzipped TCX,
/// gzipped FIT). Nothing else in the archive (contacts, comments, messages, photos) is opened.
/// </summary>
public static class StravaImporter
{
    // Outings are added this many to a transaction, then covered in passes of the same size.
    private const int Batch = 100;

    /// <param name="progress">Hears how far it has got, for the command line.</param>
    /// <exception cref="InvalidDataException">The zip isn't a Strava export (it has no activities.csv).</exception>
    public static async Task<StravaImport> ImportAsync(
        NpgsqlDataSource dataSource, string archivePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        using var archive = ZipFile.OpenRead(archivePath);
        var activities = Activities(archive);

        var importedByKind = new Dictionary<string, int>();
        var skippedByType = new Dictionary<string, int>();
        var notRead = new List<(string, string)>();
        var alreadyThere = 0;
        var deleted = 0;
        var outside = 0;

        var toImport = activities.Where(a => a.Kind is not null && a.File is not null).ToList();
        foreach (var activity in activities.Where(a => a.Kind is null || a.File is null))
            skippedByType[activity.Type] = skippedByType.GetValueOrDefault(activity.Type) + 1;
        progress?.Report($"{activities.Count:N0} activities: {toImport.Count:N0} runs, walks, hikes and rides to import");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var batch in toImport.Chunk(Batch))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var activity in batch)
            {
                if (archive.GetEntry(activity.File!) is not { } entry)
                {
                    notRead.Add((activity.Describe(), "its file isn't in the archive"));
                    continue;
                }
                // Each activity on its own: one that fails leaves the rest of its batch.
                await transaction.SaveAsync("activity", cancellationToken);
                try
                {
                    RecordedTrack track;
                    await using (var file = entry.Open()) track = RecordedTrack.Read(file, entry.Name);
                    var (_, adding) = await OutingImporter.AddAsync(
                        connection, transaction, track, activity.Kind!, activity.Name, activity.File!, activity.Id, cancellationToken);
                    if (adding == Adding.OutsideEnglandAndWales) outside++;
                    else if (adding == Adding.Deleted) deleted++;
                    else if (adding == Adding.Added) importedByKind[activity.Kind!] = importedByKind.GetValueOrDefault(activity.Kind!) + 1;
                    else alreadyThere++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    await transaction.RollbackAsync("activity", cancellationToken);
                    notRead.Add((activity.Describe(), e is InvalidDataException ? e.Message : $"{e.GetType().Name}: {e.Message}"));
                }
            }
            await transaction.CommitAsync(cancellationToken);
            progress?.Report($"read {Math.Min(toImport.IndexOf(batch[^1]) + 1, toImport.Count):N0} of {toImport.Count:N0}");
        }

        // Every Outing not yet covered, not just this run's: an import stopped part-way left some,
        // and running it again finishes them. Each batch commits, so stopping here loses little.
        var uncovered = new List<int>();
        await using (var query = new NpgsqlCommand("select id from app.outings where coverage is null order by id", connection))
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) uncovered.Add(reader.GetInt32(0));
        }
        foreach (var batch in uncovered.Chunk(Batch))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await OutingImporter.CoverAsync(connection, transaction, batch, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            progress?.Report($"covered {uncovered.IndexOf(batch[^1]) + 1:N0} of {uncovered.Count:N0} Outings");
        }

        // Then scored, each on its own (T097): stopped, running it again finishes them.
        await OutingScoring.ScoreAsync(dataSource, progress: (done, of) =>
        {
            if (done % 100 == 0 || done == of) progress?.Report($"scored {done:N0} of {of:N0} Outings");
        }, cancellationToken: cancellationToken);

        return new StravaImport(importedByKind, alreadyThere, skippedByType, notRead, outside, deleted);
    }

    private sealed record StravaActivity(long Id, DateTimeOffset? Date, string Name, string Type, string? File, string? Kind)
    {
        public string Describe() => $"{Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "undated"} {Name}";
    }

    // activities.csv: quoted fields, commas inside names, some column names twice (the first is
    // Strava's summary, the second the detail), dates like "Oct 17, 2026, 8:16:06 AM" in UTC.
    private static List<StravaActivity> Activities(ZipArchive archive)
    {
        var csv = archive.GetEntry("activities.csv") ?? throw new InvalidDataException("it isn't a Strava export: there's no activities.csv in it");
        using var stream = csv.Open();
        using var parser = new TextFieldParser(stream) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new InvalidDataException("its activities.csv is empty");
        int Column(string name) => Array.IndexOf(header, name) is >= 0 and var i ? i : throw new InvalidDataException($"its activities.csv has no '{name}' column");
        int id = Column("Activity ID"), date = Column("Activity Date"), name = Column("Activity Name"), type = Column("Activity Type"), file = Column("Filename");

        var activities = new List<StravaActivity>();
        while (parser.ReadFields() is { } fields)
        {
            var activityType = fields[type];
            activities.Add(new StravaActivity(
                long.Parse(fields[id], CultureInfo.InvariantCulture),
                DateTimeOffset.TryParse(fields[date], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : null,
                fields[name],
                activityType,
                string.IsNullOrWhiteSpace(fields[file]) ? null : fields[file],
                // A ride on a trainer has no way on the ground, whatever Strava calls the sport.
                activityType.StartsWith("Virtual", StringComparison.OrdinalIgnoreCase) ? null : OutingImporter.KindOf(activityType)));
        }
        return activities;
    }
}
