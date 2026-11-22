using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The map services' API keys kept in the database (T169, #173; user: "manage their API keys in order to
/// know which ones are required and for them to be added, changed and deleted"; kept in the database, the
/// user's choice): saved, changed and deleted by name. On the test database only, with invented keys.
/// </summary>
[TestFixture]
[NonParallelizable]
public class KeepingApiKeys
{
    private NpgsqlDataSource _dataSource = null!;
    private ApiKeys _keys = null!;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _keys = new ApiKeys(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [SetUp]
    public async Task NoKeys()
    {
        await using var command = _dataSource.CreateCommand("delete from app.api_keys");
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task A_saved_key_is_kept_by_its_name()
    {
        await _keys.SaveAsync("OsMaps:ApiKey", "os-test-key");
        Assert.That(await _keys.AllAsync(), Is.EqualTo(new Dictionary<string, string> { ["OsMaps:ApiKey"] = "os-test-key" }));
    }

    [Test]
    public async Task Saved_again_it_is_changed()
    {
        await _keys.SaveAsync("OsMaps:ApiKey", "os-test-key");
        await _keys.SaveAsync("OsMaps:ApiKey", "os-other-key");
        Assert.That(await _keys.AllAsync(), Is.EqualTo(new Dictionary<string, string> { ["OsMaps:ApiKey"] = "os-other-key" }));
    }

    [Test]
    public async Task Deleted_it_is_gone_and_the_others_stay()
    {
        await _keys.SaveAsync("OsMaps:ApiKey", "os-test-key");
        await _keys.SaveAsync("Thunderforest:ApiKey", "tf-test-key");
        await _keys.DeleteAsync("OsMaps:ApiKey");
        Assert.That(await _keys.AllAsync(), Is.EqualTo(new Dictionary<string, string> { ["Thunderforest:ApiKey"] = "tf-test-key" }));
    }

    //  The words around a key pasted from a web page aren't part of it.
    [Test]
    public async Task Saved_with_spaces_around_it_they_are_trimmed()
    {
        await _keys.SaveAsync("OsMaps:ApiKey", "  os-test-key \n");
        Assert.That((await _keys.AllAsync())["OsMaps:ApiKey"], Is.EqualTo("os-test-key"));
    }
}
