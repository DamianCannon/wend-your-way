using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// What the app needs to start `wend update` (T128): Wend Your Way's folder, found upwards from the app's
/// own, and whether `wend` was built from the same code as the app (builds are deterministic, so the
/// same library is the same bytes).
/// </summary>
public class FindingWhatToUpdateWith
{
    private string _folder = null!;

    [SetUp]
    public void Arrange() => _folder = Directory.CreateTempSubdirectory("cutthrough-root-").FullName;

    [TearDown]
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void WendYourWays_folder_is_found_above_the_apps()
    {
        File.WriteAllText(Path.Combine(_folder, "WendYourWay.sln"), "");
        var app = Directory.CreateDirectory(Path.Combine(_folder, "src", "WendYourWay.Web")).FullName;
        Assert.That(WendLauncher.RepositoryRoot(app), Is.EqualTo(_folder));
    }

    [Test]
    public void With_no_solution_above_there_is_none() =>
        Assert.That(WendLauncher.RepositoryRoot(Directory.CreateDirectory(Path.Combine(_folder, "elsewhere")).FullName), Is.Null.Or.Not.StartWith(_folder));

    [Test]
    public void The_same_bytes_are_the_same_code()
    {
        var (a, b) = (Path.Combine(_folder, "a.dll"), Path.Combine(_folder, "b.dll"));
        File.WriteAllBytes(a, [1, 2, 3]);
        File.WriteAllBytes(b, [1, 2, 3]);
        Assert.That(WendLauncher.SameCode(a, b), Is.True);
    }

    [Test]
    public void Other_bytes_are_other_code()
    {
        var (a, b) = (Path.Combine(_folder, "a.dll"), Path.Combine(_folder, "b.dll"));
        File.WriteAllBytes(a, [1, 2, 3]);
        File.WriteAllBytes(b, [1, 2, 4]);
        Assert.That(WendLauncher.SameCode(a, b), Is.False);
    }

    [Test]
    public void One_missing_isnt_the_same_code() =>
        Assert.That(WendLauncher.SameCode(Path.Combine(_folder, "a.dll"), typeof(ProcessMapUpdater).Assembly.Location), Is.False);
}
