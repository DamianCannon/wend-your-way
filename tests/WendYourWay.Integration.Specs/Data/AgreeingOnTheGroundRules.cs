using System.Text.RegularExpressions;
using WendYourWay.Core.Network;
using WendYourWay.Data;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The rules the Network is built by, said in the list of rules (T200, #204), are the database's: each of
/// <see cref="GroundRules"/> read against the SQL that builds the Network, so neither changes without the other. No
/// database: the SQL as the app applies it, from the Data assembly.
/// </summary>
public partial class AgreeingOnTheGroundRules
{
    private static string Sql(string file)
    {
        var assembly = typeof(Migrations).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Functions." + file, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    private static double Constant(string sql, string name) =>
        double.Parse(Regex.Match(sql, $@"\b{name}\s+constant double precision := ([\d.]+);").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public void Beside_a_road() => Assert.That(Constant(Sql("group_same_ground.sql"), "c_within_m"), Is.EqualTo(GroundRules.BesideM));

    [Test]
    public void Beside_a_big_road() => Assert.That(Constant(Sql("group_same_ground.sql"), "c_big_within_m"), Is.EqualTo(GroundRules.BesideBigRoadM));

    [Test]
    public void Mostly() => Assert.That(Constant(Sql("group_same_ground.sql"), "c_most"), Is.EqualTo(GroundRules.MostlyShare));

    [Test]
    public void Fast() => Assert.That(Sql("group_same_ground.sql"), Does.Contain($"mph$')::int >= {GroundRules.FastMph}"));

    [Test]
    public void A_real_pavement() => Assert.That(Sql("classify_ground.sql"), Does.Contain($"split_part(p_tags ->> 'maxspeed', ' ', 1)::int <= {GroundRules.PavementMph}"));

    [Test]
    public void Unpaved()
    {
        var list = Regex.Match(Sql("classify_ground.sql"), @"p_tags ->> 'surface' in \(([^)]*)\)").Groups[1].Value;
        Assert.That(QuotedRegex().Matches(list).Select(m => m.Groups[1].Value), Is.EqualTo(GroundRules.Unpaved));
    }

    [Test]
    public void Main_Roads()
    {
        var list = Regex.Match(Sql("build_segments.sql"), @"-- A Main Road: heavy traffic[^\n]*\n\s*coalesce\(w.tags->>'highway' in \(([^)]*)\)").Groups[1].Value;
        Assert.That(QuotedRegex().Matches(list).Select(m => m.Groups[1].Value),
            Is.EqualTo(GroundRules.MainRoads.SelectMany(r => new[] { r, r + "_link" })));
    }

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex QuotedRegex();
}
