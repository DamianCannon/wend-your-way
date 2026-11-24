using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// OS's open data fetched for a map update (T171, #175): OS Open Names for searching places and OS Terrain 50
/// for heights, from OS's Downloads API, free and without a key. A release is told by its version, and
/// its file by its MD5.
/// </summary>
public class FetchingOsData
{
    private static readonly byte[] Zip = [1, 2, 3, 4, 5];

    [Test]
    public async Task A_products_release_is_its_version_and_its_GB_file_in_the_format_asked()
    {
        using var stub = new OsDownloadsStub().Offering("OpenNames", "2026-10", "CSV", "opname_csv_gb.zip", Zip);
        using var http = new HttpClient(stub);
        var release = await new OsDownloads(http, OsDownloadsStub.Api).ReleaseAsync(OsDownloads.Names);
        Assert.That((release.Version, release.FileName, release.Md5, release.Size),
            Is.EqualTo(("2026-10", "opname_csv_gb.zip", OsDownloadsStub.Md5Of(Zip), (long)Zip.Length)));
    }

    [Test]
    public async Task A_releases_file_is_downloaded_into_the_folder_by_its_name_and_checked_against_its_MD5()
    {
        using var stub = new OsDownloadsStub().Offering("OpenNames", "2026-10", "CSV", "opname_csv_gb.zip", Zip);
        using var http = new HttpClient(stub);
        var folder = Directory.CreateTempSubdirectory("wend-os-").FullName;
        try
        {
            var os = new OsDownloads(http, OsDownloadsStub.Api);
            var kept = await os.DownloadAsync(await os.ReleaseAsync(OsDownloads.Names), folder);
            Assert.That((Path.GetFileName(kept), await File.ReadAllBytesAsync(kept)), Is.EqualTo(("opname_csv_gb.zip", Zip)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    //  Its file kept already (Terrain 50's, unzipped by hand before T171): not downloaded again.
    [Test]
    public async Task A_file_kept_with_the_releases_MD5_isnt_downloaded_again()
    {
        using var stub = new OsDownloadsStub().Offering("Terrain50", "2026-07", "ASCII Grid and GML (Grid)", "terr50_gagg_gb.zip", Zip);
        using var http = new HttpClient(stub);
        var folder = Directory.CreateTempSubdirectory("wend-os-").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, "terr50_gagg_gb.zip"), Zip);
            var os = new OsDownloads(http, OsDownloadsStub.Api);
            await os.DownloadAsync(await os.ReleaseAsync(OsDownloads.Heights), folder);
            Assert.That(stub.Asked, Has.None.Contains("redirect"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public async Task A_download_that_doesnt_match_its_MD5_changes_nothing()
    {
        using var stub = new OsDownloadsStub { WrongMd5 = "0123456789abcdef0123456789abcdef" }.Offering("OpenNames", "2026-10", "CSV", "opname_csv_gb.zip", Zip);
        using var http = new HttpClient(stub);
        var folder = Directory.CreateTempSubdirectory("wend-os-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "opname_csv_gb.zip"), "before");
            var os = new OsDownloads(http, OsDownloadsStub.Api);
            var release = await os.ReleaseAsync(OsDownloads.Names);
            await Assert.ThrowsAsync<ExtractNotDownloadedException>(() => os.DownloadAsync(release, folder));
            Assert.That((await File.ReadAllTextAsync(Path.Combine(folder, "opname_csv_gb.zip")), Directory.GetFiles(folder).Length), Is.EqualTo(("before", 1)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
