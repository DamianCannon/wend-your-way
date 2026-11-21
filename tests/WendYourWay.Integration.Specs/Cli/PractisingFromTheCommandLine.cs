using WendYourWay.Cli;
using WendYourWay.Data;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend practice` (T161, #165): the practice map built in its own database if it isn't there, said
/// ready if it is, built again with --rebuild. On the test database, its own practice database.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PractisingFromTheCommandLine
{
    private static async Task<(int ExitCode, string Said)> Wend(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run([.. args, "--database", TestDatabase.ConnectionString, "--osm2pgsql", Tools.Osm2pgsql], output);
        return (exitCode, output.ToString());
    }

    [Test]
    public async Task With_no_map_it_builds_it()
    {
        TestDatabase.Wipe();
        var (exitCode, said) = await Wend("practice");
        Assert.That((exitCode, said.Contains("Building the practice map", StringComparison.Ordinal), await PracticeMap.IsBuiltAsync(TestDatabase.ConnectionString)),
            Is.EqualTo((0, true, true)));
    }

    [Test]
    public async Task Built_already_it_says_it_s_ready_without_building_it_again()
    {
        await Wend("practice");
        var (exitCode, said) = await Wend("practice");
        Assert.That((exitCode, said.Contains("Building", StringComparison.Ordinal), said.StartsWith("The practice map is ready: Richmond Park and around", StringComparison.Ordinal)),
            Is.EqualTo((0, false, true)));
    }

    [Test]
    public async Task With_rebuild_it_builds_it_again()
    {
        await Wend("practice");
        var (exitCode, said) = await Wend("practice", "--rebuild");
        Assert.That((exitCode, said.Contains("Building the practice map", StringComparison.Ordinal)), Is.EqualTo((0, true)));
    }

    //  Its database not there, and the role can't make one (as on Windows): it says what to run.
    [Test]
    public async Task With_no_database_and_none_to_be_made_it_says_to_run_setup_again()
    {
        using var output = new StringWriter();
        var nowhere = new Npgsql.NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString) { Database = "cutthrough_nowhere_test" }.ConnectionString;
        Assume.That(await CanMakeDatabasesAsync(), Is.False, "a role that can't make databases, as setup.ps1 makes it");
        var exitCode = await CommandLine.Run(["practice", "--database", nowhere], output);
        Assert.That((exitCode, output.ToString().Trim()), Is.EqualTo((1, PracticeCommand.NoDatabase)));
    }

    private static async Task<bool> CanMakeDatabasesAsync()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var query = dataSource.CreateCommand("select rolcreatedb or rolsuper from pg_roles where rolname = current_user");
        return (bool)(await query.ExecuteScalarAsync())!;
    }
}
