using WendYourWay.Data.Building;

namespace WendYourWay.Data;

/// <summary>
/// The app's database readied as it starts (#153, T149). Its migrations, and its function scripts, are
/// applied, unless a map update, a build or another job holds the update lock: changed under one, the map
/// it swaps in would lack what the app now reads. Then they're held back, the app opening on the map as it
/// is, and made when it next starts with nothing running. Its start-up backup waits for that too.
/// </summary>
public static class StartUp
{
    /// <summary>What was readied: migrations applied, or held back for a job running.</summary>
    public sealed record Prepared(bool JobRunning, IReadOnlyList<string> Migrated, IReadOnlyList<string> HeldBack);

    public static async Task<Prepared> PrepareAsync(string connectionString, CancellationToken cancellationToken = default) =>
        await UpdateLock.IsHeldAsync(connectionString, cancellationToken)
            ? new Prepared(true, [], Migrations.Pending(connectionString))
            : new Prepared(false, Migrations.Apply(connectionString), []);
}
