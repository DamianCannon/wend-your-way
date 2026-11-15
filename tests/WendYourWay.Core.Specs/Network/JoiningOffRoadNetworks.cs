using WendYourWay.Core.Network;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// Off-road networks over Segments as stored, for the build (PLAN T022): fed green Segments and
/// then roads, by OSM node id, they come out as <see cref="OffRoadNetworks"/> finds them in a slice.
/// </summary>
public class JoiningOffRoadNetworks
{
    //  A path joined to another by a 100 m crossing (B=C); a 200 m road (D=E) too long to join
    //  E's path; two 100 m roads in a row (G=H=I) not joining the paths at their ends; a pocket
    //  path (L-M) on its own.
    private const string Map = """
        A.........B=C.........D===E.....N

        F.....G=H=I.....P

        L-----M
        """;

    private static (OffRoadNetworks Slice, OffRoadJoining.Joined Joined, NetworkSlice Drawn) Both(string map)
    {
        var drawn = AsciiNetwork.Parse(map).Slice;
        var joining = new OffRoadJoining();
        foreach (var s in drawn.Segments.Where(OffRoadNetworks.IsGreen)) joining.AddGreen(s.From, s.To, s.LengthM);
        foreach (var s in drawn.Segments.Where(s => !OffRoadNetworks.IsGreen(s) && s.Access != AccessKind.Forbidden)) joining.AddRoad(s.From, s.To, s.LengthM);
        return (OffRoadNetworks.Of(drawn), joining.Finish(), drawn);
    }

    [Test]
    public void Then_its_networks_are_the_slices()
    {
        var (slice, joined, drawn) = Both(Map);
        var green = drawn.Segments.Where(OffRoadNetworks.IsGreen).ToList();
        //  Two green Segments share a network in one exactly when they do in the other.
        var pairs = from i in Enumerable.Range(0, green.Count)
                    from j in Enumerable.Range(0, green.Count)
                    select (slice.NetworkAt(green[i].From) == slice.NetworkAt(green[j].From)) == (joined.NetworkOf[i] == joined.NetworkOf[j]);
        Assert.That(pairs, Is.All.True);
    }

    [Test]
    public void Then_each_network_measures_its_green_ground_as_the_slice_does()
    {
        var (slice, joined, drawn) = Both(Map);
        var green = drawn.Segments.Where(OffRoadNetworks.IsGreen).ToList();
        Assert.That(Enumerable.Range(0, green.Count).Select(i => joined.Metres[joined.NetworkOf[i]]),
            Is.EqualTo(green.Select(s => slice.MetresAt(s.From))));
    }

    [Test]
    public void Then_there_are_as_many_networks_as_in_the_slice()
    {
        var (slice, joined, drawn) = Both(Map);
        Assert.That(joined.Metres, Has.Length.EqualTo(drawn.Segments.Where(OffRoadNetworks.IsGreen).Select(s => slice.NetworkAt(s.From)).Distinct().Count()));
    }

    [Test]
    public void A_road_given_before_the_last_green_Segment_is_refused()
    {
        var joining = new OffRoadJoining();
        joining.AddRoad(1, 2, 50);
        Assert.That(() => joining.AddGreen(2, 3, 50), Throws.InvalidOperationException);
    }
}
