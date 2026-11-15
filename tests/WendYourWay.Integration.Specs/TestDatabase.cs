using Npgsql;

namespace WendYourWay.Integration.Specs;

/// <summary>
/// The integration specs' own database, <c>cutthrough_test</c>, created with PostGIS by
/// scripts/setup.ps1. It is wiped at the start of every run; the real <c>cutthrough</c>
/// database is never touched. Override with the WEND_TEST_DB environment variable.
/// </summary>
[SetUpFixture]
public class TestDatabase
{
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("WEND_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=cutthrough_test;Username=cutthrough";

    [OneTimeSetUp]
    public void WipeOnce() => Wipe();

    /// <summary>
    /// Drops everything the app's role owns in the test database: schemas, tables, types and the
    /// migration journal. PostGIS stays, because the superuser owns it. Refuses to run against any
    /// database whose name doesn't end in "_test".
    /// </summary>
    public static void Wipe()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (builder.Database is null || !builder.Database.EndsWith("_test", StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to wipe '{builder.Database}': only databases named *_test are wiped.");

        try
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand($"drop owned by {builder.Username}", connection);
            command.ExecuteNonQuery();
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            throw new InvalidOperationException(
                $"The test database '{builder.Database}' doesn't exist. Run scripts/setup.ps1 (as administrator) to create it.", e);
        }
    }
}
