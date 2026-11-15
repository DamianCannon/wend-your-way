using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Support;

namespace WendYourWay.Data;

/// <summary>
/// Applies the SQL migrations in db/migrations (embedded in this assembly) in name order, each in
/// its own transaction. Already-applied migrations are recorded in the journal and skipped. Then
/// re-applies every function definition in db/functions, so a function changes by editing its
/// script rather than by copying it into a new migration.
/// </summary>
public static class Migrations
{
    /// <summary>Applies any migrations not yet applied and every function script, and returns the migrations' names.</summary>
    /// <exception cref="InvalidOperationException">A migration failed; nothing after it was applied.</exception>
    public static IReadOnlyList<string> Apply(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(Migrations).Assembly, IsIn(".Migrations."),
                new SqlScriptOptions { ScriptType = ScriptType.RunOnce, RunGroupOrder = 1 })
            .WithScriptsEmbeddedInAssembly(typeof(Migrations).Assembly, IsIn(".Functions."),
                new SqlScriptOptions { ScriptType = ScriptType.RunAlways, RunGroupOrder = 2 })
            .WithTransactionPerScript()
            .LogTo(new NoOpUpgradeLog())
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException(
                $"Migration {result.ErrorScript?.Name ?? "(unknown)"} failed: {result.Error.Message}", result.Error);

        return [.. result.Scripts.Where(script => script.SqlScriptOptions.ScriptType == ScriptType.RunOnce).Select(script => script.Name)];
    }

    /// <summary>
    /// The last migration this build of Wend Your Way has, by name (they apply in name order): what a
    /// backup records, so one from a newer Wend Your Way isn't restored into an older database (T102).
    /// </summary>
    public static string Latest { get; } = typeof(Migrations).Assembly.GetManifestResourceNames()
        .Where(IsIn(".Migrations.")).Order(StringComparer.Ordinal).Last();

    private static Func<string, bool> IsIn(string folder) =>
        name => name.Contains(folder, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal);
}
