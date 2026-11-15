using WendYourWay.Data;
using WendYourWay.Data.Building;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// How long the last job of a kind took (#142, T138, STORY-049 AC8): the planner says "about 3 h 25
/// min last time" beside a job under way, from the last of its kind that ended well.
/// </summary>
[TestFixture]
[NonParallelizable]
public class TimingTheJobs
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var jobs = _dataSource.CreateCommand("""
            insert into app.map_updates (kind, stage, outcome, source, started_at, finished_at) values
              ('update', 'backing up', 'finished',   'spec', '2026-11-01T09:00Z', '2026-11-01T12:25Z'),
              ('update', 'checking',   'up to date', 'spec', '2026-11-05T09:00Z', '2026-11-05T09:01Z'),
              ('update', 'building',   'failed',     'spec', '2026-11-08T09:00Z', '2026-11-08T10:00Z'),
              ('import', 'reading',    'finished',   'spec', '2026-11-09T09:00Z', '2026-11-09T09:02Z'),
              ('update', 'building',   'running',    'spec', '2026-11-15T09:00Z', null)
            """);
        await jobs.ExecuteNonQueryAsync();
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public async Task The_last_of_its_kind_that_ended_well_is_found() =>
        Assert.That((await new Jobs(_dataSource).LastEndedWellAsync(JobKind.Update))?.StartedAt, Is.EqualTo(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero)));

    [Test]
    public async Task One_of_another_kind_isnt() =>
        Assert.That((await new Jobs(_dataSource).LastEndedWellAsync(JobKind.Restore)), Is.Null);
}
