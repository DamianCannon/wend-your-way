using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// The user's seven rated Trail Router Loops (2026-10-18) as the first Reference Loop spec, pulled
/// forward from increment 5 to guard increment 1's scoring: every Loop they called very good must
/// outscore every one they called good. Read from the git-ignored data/reference-loops/ (the Loops
/// start at home), matched onto the app's own Network, and scored by the app's own scorer. Only
/// file names and scores are ever printed, never a position or the user's reasons, which name
/// places. Skipped where the folder isn't. It reads the live Network, so a monthly rebuild can
/// move its results; increment 5 snapshots Loops and slices to fixtures.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class RankingTheUsersRatedLoops
{
    private sealed record Rated(string File, string Verdict, MatchedRoute Matched, double Score, Breakdown Breakdown, NpgsqlDataSource Network)
    {
        //  Trail Router names its exports route_<time>.gpx; the user names the generator's.
        public bool ByTrailRouter => File.StartsWith("route_", StringComparison.Ordinal);
    }

    //  The user's verdicts, worst first: "good or very good" is how they rate every Trail Router
    //  Loop added for T040 (2026-10-23).
    private static readonly string[] VerdictsWorstFirst = ["very bad", "poor", "ok", "good", "good or very good", "very good"];

    private readonly List<Rated> _rated = [];
    private NpgsqlDataSource? _network;
    private NpgsqlDataSource? _other;

    //  WEND_REFERENCE_LOOPS, or data/reference-loops above wherever the specs run from.
    private static string? Folder()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("WEND_REFERENCE_LOOPS");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(Path.Combine(fromEnvironment, "verdicts.md"))) return fromEnvironment;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var folder = Path.Combine(dir.FullName, "data", "reference-loops");
            if (File.Exists(Path.Combine(folder, "verdicts.md"))) return folder;
        }
        return null;
    }

    //  The file and verdict columns of the verdicts table; the third holds the user's reasons.
    private static IEnumerable<(string File, string Verdict)> Verdicts(string folder) =>
        File.ReadLines(Path.Combine(folder, "verdicts.md"))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(cells => cells.Length >= 3 && cells[1].EndsWith(".gpx", StringComparison.OrdinalIgnoreCase))
            .Select(cells => (cells[1], cells[2]));

    [OneTimeSetUp]
    public async Task Arrange()
    {
        var folder = Folder();
        if (folder is null) Assert.Ignore("No data/reference-loops/verdicts.md: the user's rated Loops aren't on this machine.");
        //  London's Network first, where the benchmark's Loops were made and new rules are measured
        //  first (WEND_REAL_DATABASE overrides it): two Loops from one start must be scored on
        //  one Network.
        var first = Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.London;
        _network = await RealNetwork.OpenOrIgnore(first);
        foreach (var (file, verdict) in Verdicts(folder!))
        {
            LatLon[] route = [.. RecordedTrack.Read(Path.Combine(folder!, file)).Points.Select(p => new LatLon(p.Latitude, p.Longitude))];
            var network = _network;
            var matched = await new RouteMatching(network).MatchAsync(route);
            //  A Loop the Network matches poorly is matched on the other one too, and the better
            //  kept: one outside London's Network on Great Britain's (user, 2026-10-23), and one
            //  generated with Open ground on London's until Great Britain's has it (T039).
            static double Miss(MatchedRoute m) => Math.Max(Math.Abs((m.Match.LengthM / m.RouteLengthM) - 1), 1 - m.ShareNearMatch);
            if (Miss(matched) > 0.02)
            {
                _other ??= await RealNetwork.OpenOrIgnore(first == RealNetwork.London ? RealNetwork.GreatBritain : RealNetwork.London);
                var there = await new RouteMatching(_other).MatchAsync(route);
                if (Miss(there) < Miss(matched)) (matched, network) = (there, _other);
            }
            _rated.Add(new Rated(file, verdict, matched,
                // As a Run's Loops are ranked (T105): the score and the blend of what the user likes.
                LoopScorer.Score(matched.Slice, matched.Match.Loop, ScoringWeights.Default.Ranking(Core.Loops.Activity.Run)),
                Breakdown.Of(matched.Slice, matched.Match.Loop), network));
        }
    }

    [OneTimeTearDown]
    public async Task Dispose()
    {
        if (_network is not null) await _network.DisposeAsync();
        if (_other is not null) await _other.DisposeAsync();
    }

    private void Report()
    {
        foreach (var r in _rated.OrderByDescending(r => r.Score))
        {
            TestContext.Out.WriteLine(
                $"{(r.ByTrailRouter ? "Trail Router" : "Wend Your Way"),-12} {r.File,-26} {r.Verdict,-17} score {r.Score:0.000} · {r.Breakdown.LengthM / 1000:0.00} km · Off-road {r.Breakdown.OffRoadPercent:0}% · " +
                $"Traffic-free {r.Breakdown.TrafficFreePercent:0}% · Connector {r.Breakdown.ConnectorLengthM / 1000:0.00} km · Greenery {r.Breakdown.GreeneryPercent:0}% · Waterside {r.Breakdown.WatersidePercent:0}% · Corridor {r.Breakdown.CorridorLengthM / 1000:0.00} km · Open ground {r.Breakdown.OpenGroundM / 1000:0.00} km · " +
                $"Flow {r.Breakdown.FlowM / 1000:0.00} km · Main Road {r.Breakdown.MainRoadLengthM / 1000:0.00} km · Retrace {r.Breakdown.RetraceLengthM / 1000:0.00} km · " +
                $"knots {Core.Scoring.Knots.Of(r.Matched.Slice, r.Matched.Match.Loop)} · " +
                $"matched {r.Matched.Match.LengthM / r.Matched.RouteLengthM:P1} of the route's length, {r.Matched.ShareNearMatch:P1} of it near the match, " +
                $"{r.Matched.Match.Unjoined.Count} unjoined" +
                string.Concat(r.Matched.Match.Unjoined.Select(u => $"; unjoined {u.RouteM:0} m of route, the corridor's way {(u.JoinedM is null ? "none" : $"{u.JoinedM:0} m")}")));
        }
    }

    [Test]
    public void Then_every_verdict_is_one_the_specs_know() =>
        Assert.That(_rated.Select(r => r.Verdict).Except(VerdictsWorstFirst), Is.Empty);

    //  The scores mean nothing unless each match is the route. Most match within 2%. One of the
    //  first seven can't be joined for about 0.7 km in two places, where it crosses a trunk road by
    //  a bridge: every way there is in the Network, never more than 15 m from the route, but they
    //  don't join up inside the corridor (most likely the paths meet the bridge without a shared
    //  node in today's OSM, which Trail Router's older data had). Up to 6% may go unmatched, each
    //  stretch named; fourteen of T040's Loops miss more, up to 15%, and are named here.
    private static readonly string[] LooselyMatched = ["route_1792761985585.gpx", "bridges-1792761523983.gpx", "bridges-1792761985585.gpx", "bridges-1792761907384.gpx", "trimmed-1792761907384.gpx", "trimmed-1792761985585.gpx", "crossings-1792761985585.gpx", "new-1792761985585.gpx", "spike-1792761764447.gpx", "today-1792761886754.gpx", "today-1792761907384.gpx", "route_1792762254870.gpx", "primrose-hill-9.4km.gpx", "trent-park-10.8km.gpx"];

    [Test]
    public void Then_every_rated_Loop_matches_its_route()
    {
        Report();
        double Tolerance(Rated r) => LooselyMatched.Contains(r.File) ? 0.15 : 0.06;
        Assert.That(_rated.Where(r => Math.Abs((r.Matched.Match.LengthM / r.Matched.RouteLengthM) - 1) > Tolerance(r)
                || r.Matched.ShareNearMatch < 1 - Tolerance(r)).Select(r => r.File),
            Is.Empty);
    }

    //  T040's scoreboard, now the guard on the score (T042): pairs of Loops from the same start
    //  (within 500 m), of about the same length (within 15%, the generator only ever chooses
    //  between those), whose verdicts differ: good or better above below good, and very good above
    //  good. Scores compare only from the same start: they measure the place as much as the Loop.
    //  Before T042 the score ordered 22 of 49 against the user's verdicts (within 30%).
    //  The causes of those left (T042-T045): the worth of a standout place near home ("good use
    //  of the Heath"); which water and which streets beside it ("gets a lot of canal", "that knot
    //  around Charing Cross"); two lobes crossing without a junction ("a bit of a mess"); spurs in
    //  Loops rated before they were cut out; and fine distinctions between the generator's own
    //  good and very good Loops. None may be added: the count can only fall. 36 by the score alone;
    //  40 since Runs are ranked with the blend of off-road and climbing (T105), which the user took
    //  ("on balance the blend runs look a little better so yes let's go").
    private const int KnownMisorders = 40;

    private static double Rank(string verdict) => Array.IndexOf(VerdictsWorstFirst, verdict) switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 3.5, _ => 4 };

    [Test]
    public void Then_no_pair_from_the_same_start_is_ordered_against_the_users_verdicts_but_those_known()
    {
        var wrong = Misordered(r => r.Score);
        TestContext.Out.WriteLine($"{wrong.Count} pairs ordered against the user's verdicts: {string.Join(", ", wrong)}");
        Assert.That(wrong, Has.Count.LessThanOrEqualTo(KnownMisorders));
    }

    private List<string> Misordered(Func<Rated, double> score)
    {
        var loops = _rated.Select(r => (r.File, Rank: Rank(r.Verdict), Start: r.Matched.Slice.Node(r.Matched.Match.Loop.Start), r.Breakdown.LengthM, Score: score(r))).ToList();
        return (from a in loops
                     from b in loops
                     where a.Start.DistanceTo(b.Start) < 500 && Math.Abs(a.LengthM - b.LengthM) < 0.15 * Math.Max(a.LengthM, b.LengthM)
                       && ((a.Rank >= 3 && b.Rank <= 2) || (a.Rank == 4 && b.Rank == 3))
                       && a.Score <= b.Score
                     select $"{a.File} > {b.File}").ToList();
    }

    //  The Off-road Lean (PLAN T058): from each Trail Router Loop's start, for its length, how Loop 1
    //  splits between Off-road, Traffic-free and road at each Lean (the centre alone measured first).
    [Test]
    [Explicit("A measurement of Off-road against Traffic-free")]
    public async Task Off_road_and_Traffic_free_from_each_Trail_Router_Loops_start()
    {
        foreach (var r in _rated.Where(r => r.ByTrailRouter))
        {
            foreach (var lean in new[] { -1, -0.5, 0, 0.5, 1 })
            {
                var request = new Core.Loops.Request(r.Matched.Slice.Node(r.Matched.Match.Loop.Start), r.Breakdown.LengthM / 1000, OffRoadLean: lean);
                var slice = await new PostgisNetworkSlices(r.Network).AroundAsync(request.Start, request.SliceRadiusM);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                if (Core.Loops.LoopGenerator.Generate(slice, request, ScoringWeights.Default) is not { } loop) continue;
                var took = clock.Elapsed.TotalSeconds;
                var b = Breakdown.Of(slice, loop);
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                    $"SPLIT {r.File,-26} {lean,4:+0.0;-0.0;0}: Off-road {b.OffRoadPercent:0}% Traffic-free {b.TrafficFreePercent:0}% Main Road {b.MainRoadLengthM:0} m {b.LengthM / 1000:0.0} km · {took:0.0} s"));
            }
        }
    }

    //  Before a Novelty Lean (PLAN T059): from each Trail Router Loop's start, for its length, how
    //  much of Loop 1 is on ground the user's runs, walks and hikes already covered.
    [Test]
    [Explicit("A measurement of familiar ground")]
    public async Task Familiar_ground_from_each_Trail_Router_Loops_start()
    {
        var covered = new Dictionary<NpgsqlDataSource, HashSet<Core.Network.SegmentKey>>();
        foreach (var r in _rated.Where(r => r.ByTrailRouter))
        {
            if (!covered.TryGetValue(r.Network, out var keys)) covered[r.Network] = keys = await FamiliarGround.RunAsync(r.Network);
            var request = new Core.Loops.Request(r.Matched.Slice.Node(r.Matched.Match.Loop.Start), r.Breakdown.LengthM / 1000);
            var slice = await new PostgisNetworkSlices(r.Network).AroundAsync(request.Start, request.SliceRadiusM);
            if (Core.Loops.LoopGenerator.Generate(slice, request, ScoringWeights.Default) is not { } loop) continue;
            var b = Breakdown.Of(slice, loop);
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                $"FAMILIAR {r.File,-26} {b.LengthM / 1000:0.0} km: Loop 1 {FamiliarGround.Share(slice, loop, keys):P0} familiar, the Trail Router Loop {FamiliarGround.Share(r.Matched.Slice, r.Matched.Match.Loop, keys):P0}"));
        }
    }

    //  A measurement, not a check: from each Trail Router Loop's start, for its length, the Loop
    //  the generator makes, both scored the same way on the Network it was matched on, and how much
    //  of Trail Router's it shares by length. With WEND_LOOPS_OUT, both are written there as
    //  GPX to look at on a map; they may start at home, so the folder must be local. With
    //  WEND_HILLS_LEANS (say "-1,0,1"), one Loop for each Hills Lean (T050), its climbing per
    //  km and green share given.
    [Test]
    [Explicit("A measurement of the generator against Trail Router")]
    public async Task Generating_from_each_Trail_Router_Loops_start()
    {
        var output = Environment.GetEnvironmentVariable("WEND_LOOPS_OUT");
        var weights = LeaningOnHillsFromRealStarts.TriedWeights();
        double[] leans = [.. (Environment.GetEnvironmentVariable("WEND_HILLS_LEANS") ?? "0")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => double.Parse(l, System.Globalization.CultureInfo.InvariantCulture))];
        foreach (var r in _rated.Where(r => r.ByTrailRouter))
        {
            var trailRouter = r.Matched.Match.Loop.Traversals.Select(t => r.Matched.Slice.Segments[t.Segment])
                .GroupBy(s => s.Key).ToDictionary(g => g.Key, g => g.First().LengthM);
            var geometry = new MapGeometry(r.Network);
            async Task Write(string name, Core.Network.NetworkSlice on, Core.Loops.Loop written)
            {
                if (string.IsNullOrEmpty(output)) return;
                var line = await geometry.TraceAsync([.. written.Traversals.Select(t => (on.Segments[t.Segment].Key, t.Forward))]);
                await File.WriteAllTextAsync(Path.Combine(output, $"{Path.GetFileNameWithoutExtension(r.File)}-{name}.gpx"),
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<gpx version=\"1.1\" creator=\"Wend Your Way\"><trk><name>" + name + "</name><trkseg>" +
                    string.Concat(line.Select(p => FormattableString.Invariant($"<trkpt lat=\"{p.Latitude:0.000000}\" lon=\"{p.Longitude:0.000000}\"/>"))) +
                    "</trkseg></trk></gpx>\n");
            }
            await Write("1-trail-router", r.Matched.Slice, r.Matched.Match.Loop);
            foreach (var lean in leans)
            {
                var request = new Core.Loops.Request(r.Matched.Slice.Node(r.Matched.Match.Loop.Start), r.Breakdown.LengthM / 1000, lean);
                var slice = await new PostgisNetworkSlices(r.Network).AroundAsync(request.Start, request.SliceRadiusM);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var loop = Core.Loops.LoopGenerator.Generate(slice, request, weights);
                var took = clock.Elapsed;
                if (loop is null)
                {
                    await TestContext.Out.WriteLineAsync($"{r.File,-26} hills {lean,4:+0.0;-0.0;0} no Loop");
                    continue;
                }
                if (Environment.GetEnvironmentVariable("WEND_OFFER") is { Length: > 0 })
                {
                    //  T052: the five Loops offered, each against the first.
                    var leaning = weights with { HillsLean = lean };
                    var offered = Core.Loops.LoopGenerator.Offer(slice, request, weights);
                    var shared = offered.SelectMany((a, i) => offered.Skip(i + 1).Select(b => Core.Loops.SharedGround.Share(slice, a, b))).DefaultIfEmpty(0).Max();
                    await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                        $"OFFER {r.File} {offered.Count} Loops, most shared {shared:P0}: ") + string.Join(" | ", offered.Select((o, i) =>
                        {
                            var b = Breakdown.Of(slice, o);
                            var heading = Core.Loops.LoopHeading.Of(slice, o) is { } h ? FormattableString.Invariant($"{h:0}°") : "round";
                            return FormattableString.Invariant($"{i + 1}: score {LoopScorer.Score(slice, o, leaning):0.000} {b.LengthM / 1000:0.0} km green {b.OffRoadPercent + b.TrafficFreePercent:0}% Main Road {b.MainRoadLengthM:0} m heading {heading}");
                        })));
                    for (var i = 1; i < offered.Count; i++) await Write($"3-offered-{i + 1}", slice, offered[i]);
                }
                var ours = Breakdown.Of(slice, loop);
                var shares = loop.Traversals.Select(t => slice.Segments[t.Segment].Key).Distinct().Where(trailRouter.ContainsKey).Sum(k => trailRouter[k]) / trailRouter.Values.Sum();
                await TestContext.Out.WriteLineAsync(
                    $"{r.File,-26} hills {lean,4:+0.0;-0.0;0} Trail Router {r.Score:0.000} ({r.Breakdown.LengthM / 1000:0.0} km, climbing {r.Breakdown.ClimbM * 1000 / r.Breakdown.LengthM:0.0} m/km, knots {Core.Scoring.Knots.Of(r.Matched.Slice, r.Matched.Match.Loop)})" +
                    $" · ours {LoopScorer.Score(slice, loop, weights with { HillsLean = lean }):0.000} ({ours.LengthM / 1000:0.0} km, climbing {ours.ClimbM * 1000 / ours.LengthM:0.0} m/km, green {ours.OffRoadPercent + ours.TrafficFreePercent:0}%, Connector {ours.ConnectorLengthM / 1000:0.0} km, knots {Core.Scoring.Knots.Of(slice, loop)}, {(loop.Anchors.Count == 0 ? "?" : loop.Anchors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))} stops) shares {shares:P0} · {took.TotalSeconds:0.0} s");
                await Write(leans.Length == 1 && lean == 0 ? "2-cutthrough" : FormattableString.Invariant($"2-cutthrough-hills{lean:+0.0;-0.0;0}"), slice, loop);
            }
        }
    }
}
