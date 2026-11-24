using System.IO.Compression;
using WendYourWay.Data;
using WendYourWay.Data.Places;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// OS Open Names loaded by a map update (T171, #175): read straight from OS's zip, about 1 GB of CSV unpacked,
/// and which release it is kept with the places, in the same go, so the two can't disagree. Loaded by hand
/// from a folder, the release isn't known. Fixtures/open-names, zipped as OS's is.
/// </summary>
[TestFixture]
[NonParallelizable]
public class LoadingPlaceNamesFromOs
{
    private static readonly PlacesRelease October = new("2026-10", "bba5aa44ba375d39de60584099e335cd");

    private string _folder = "";
    private string _zip = "";
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _folder = Directory.CreateTempSubdirectory("wend-names-").FullName;
        _zip = Path.Combine(_folder, "opname_csv_gb.zip");
        ZipFile.CreateFromDirectory(Fixtures.Path("open-names"), _zip);
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _dataSource?.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task From_OSs_zip_its_places_are_loaded()
    {
        var loaded = await PlacesImport.LoadZipAsync(_dataSource, _zip, October);
        Assert.That((loaded.Places, loaded.Postcodes), Is.EqualTo((6L, 3L)));
    }

    [Test]
    public async Task From_OSs_zip_the_release_loaded_is_kept()
    {
        await PlacesImport.LoadZipAsync(_dataSource, _zip, October);
        Assert.That((await PlacesImport.ReleaseAsync(_dataSource))?.Release, Is.EqualTo(October));
    }

    [Test]
    public async Task Loaded_by_hand_from_a_folder_the_release_isnt_known()
    {
        await PlacesImport.LoadZipAsync(_dataSource, _zip, October);
        await PlacesImport.LoadAsync(_dataSource, OpenNames.Files(Fixtures.Path("open-names")));
        Assert.That(await PlacesImport.ReleaseAsync(_dataSource), Is.Null);
    }
}
