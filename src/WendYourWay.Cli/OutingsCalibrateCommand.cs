using System.Globalization;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>
/// `wend outings calibrate` (PLAN T114): the stars' cut points worked out again from the Outings as
/// they are and kept with the user's data. Says, per Activity, how many it cut from, the cuts before
/// and after, and how many of the user's rated Outings the stars match. Counts and cuts only.
/// </summary>
public static class OutingsCalibrateCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static async Task<int> Run(string connectionString, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await output.WriteLineAsync($"Calibrating the stars → {new NpgsqlConnectionStringBuilder(connectionString).Database}");
        try
        {
            Migrations.Apply(connectionString);
            await using var dataSource = Database.CreateDataSource(connectionString);
            var calibration = await StarCalibration.CalibrateAsync(dataSource, cancellationToken);
            await Report(output, "Runs", calibration.Run);
            await Report(output, "Rides", calibration.Ride);
            await output.WriteLineAsync("Kept: the stars are cut by these the next time the Outings or Saved Routes are opened.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("Stopped. The cuts are as they were.");
            return 1;
        }
        catch (Exception e) when (e is InvalidOperationException or NpgsqlException)
        {
            await output.WriteLineAsync($"Calibrating failed: {e.Message}");
            return 1;
        }
    }

    private static async Task Report(TextWriter output, string what, ActivityCalibration activity)
    {
        if (activity.Kept)
        {
            await output.WriteLineAsync(string.Format(Culture, "{0}: too few to cut from ({1:N0} matched, {2} needed): kept {3}",
                what, activity.From, StarCuts.LeastToCalibrate, Cuts(activity.After).Trim()));
            return;
        }
        await output.WriteLineAsync(string.Format(Culture, "{0}: cut from {1:N0} matched", what, activity.From));
        await output.WriteLineAsync("  before" + Cuts(activity.Before));
        await output.WriteLineAsync("  after " + Cuts(activity.After));
        await output.WriteLineAsync(string.Format(Culture, "  of {0:N0} you've rated: the same stars {1:N0} → {2:N0}, 2 or more apart {3:N0} → {4:N0}",
            activity.Rated, activity.SameBefore, activity.SameAfter, activity.ApartBefore, activity.ApartAfter));
    }

    private static string Cuts(IReadOnlyList<double> cuts) => string.Concat(cuts.Select(c => "  " + c.ToString("0.0000", Culture)));
}
