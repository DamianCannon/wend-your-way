using Npgsql;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A measurement, not a check (PLAN T052): how long a Request takes through the planner, the
/// Loop traced and its elevation profile drawn, from public starts on Great Britain's Network
/// (WEND_REAL_DATABASE overrides it). Each start is asked twice; the second is warm.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class TimingThePlanner
{
    private static readonly (string Name, LatLon Start, double DistanceKm)[] Starts =
    [
        ("kenwood-house", new LatLon(51.5712, -0.1677), 10),
        ("box-hill", new LatLon(51.2510, -0.3127), 20),
        ("pembroke-lodge", new LatLon(51.4426, -0.3006), 10),
        ("greenwich-park", new LatLon(51.4769, 0.0005), 10),
        ("springfield-park", new LatLon(51.5695, -0.0570), 15),
        ("wanstead-flats", new LatLon(51.5530, 0.0270), 10),
    ];

    [Test]
    [Explicit("A measurement of the planner's time")]
    public async Task Timing_a_Request_from_each_start()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var terrain = RealNetwork.Terrain();
        if (terrain is null) Assert.Ignore("No OS Terrain 50 tiles: set WEND_TERRAIN.");
        var (slices, geometry) = (new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource));
        var planner = new LoopPlanner(slices, geometry, ScoringWeights.Default, terrain);
        foreach (var (name, start, km) in Starts)
        {
            var times = new List<double>();
            IReadOnlyList<PlannedLoop> offered = [];
            for (var run = 0; run < 2; run++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                offered = await planner.PlanAsync(start, km);
                times.Add(clock.Elapsed.TotalSeconds);
            }
            // How much of the warm time is finding the Loops, the rest tracing and profiling them (T053).
            var request = new WendYourWay.Core.Loops.Request(await geometry.ToGridAsync(start), km);
            var loading = System.Diagnostics.Stopwatch.StartNew();
            var slice = await slices.AroundAsync(request.Start, request.SliceRadiusM);
            var loaded = loading.Elapsed.TotalSeconds;
            var finding = System.Diagnostics.Stopwatch.StartNew();
            WendYourWay.Core.Loops.LoopGenerator.Offer(slice, request, ScoringWeights.Default);
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant($"SPLIT {name,-17} {km,3:0} km: loading {loaded:0.00} s, finding {finding.Elapsed.TotalSeconds:0.00} s, tracing {offered.Count} Loops {times[1] - loaded - finding.Elapsed.TotalSeconds:0.00} s"));
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant($"TIME {name,-17} {km,3:0} km: {offered.Count} Loops, {times[0]:0.00} s cold, {times[1]:0.00} s warm: ") +
                string.Join(" | ", offered.Select((o, i) => FormattableString.Invariant(
                    $"{i + 1}: {o.Breakdown.LengthM / 1000:0.0} km green {o.Breakdown.OffRoadPercent + o.Breakdown.TrafficFreePercent:0}% Main Road {o.Breakdown.MainRoadLengthM:0} m"))));
            if (Environment.GetEnvironmentVariable("WEND_LOOPS_OUT") is { Length: > 0 } output)
            {
                for (var i = 0; i < offered.Count; i++)
                {
                    await File.WriteAllTextAsync(Path.Combine(output, FormattableString.Invariant($"{name}-{km:0}km-loop{i + 1}.gpx")),
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<gpx version=\"1.1\" creator=\"Wend Your Way\"><trk><name>" +
                        FormattableString.Invariant($"{name} {km:0} km, Loop {i + 1}") + "</name><trkseg>" +
                        string.Concat(offered[i].Line.Select(p => FormattableString.Invariant($"<trkpt lat=\"{p.Latitude:0.000000}\" lon=\"{p.Longitude:0.000000}\"/>"))) +
                        "</trkseg></trk></gpx>\n");
                }
            }
        }
    }
    //  PLAN T053: from a Request at the centre, how long re-ranking to each Lean takes, and how its
    //  Loops compare with a new search at that Lean.
    [Test]
    [Explicit("A measurement of re-ranking")]
    public async Task Re_ranking_from_each_start()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var terrain = RealNetwork.Terrain();
        if (terrain is null) Assert.Ignore("No OS Terrain 50 tiles: set WEND_TERRAIN.");
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default, terrain);
        static string Of(PlannedLoop loop) => FormattableString.Invariant(
            $"{loop.Breakdown.LengthM / 1000:0.00} km green {loop.Breakdown.OffRoadPercent + loop.Breakdown.TrafficFreePercent:0}% Main Road {loop.Breakdown.MainRoadLengthM:0} m climb {loop.Breakdown.ClimbM / loop.Breakdown.LengthM * 1000:0} m/km");
        // The same Loop to the user: nine tenths of each line within 15 m of the other's.
        static bool Alike(PlannedLoop a, PlannedLoop b) => Within(a.Line, b.Line) >= 0.9 && Within(b.Line, a.Line) >= 0.9;
        static double Within(IReadOnlyList<LatLon> line, IReadOnlyList<LatLon> other) =>
            line.Count(p => other.Any(q => Math.Abs(p.Latitude - q.Latitude) * 111_200 < 15 && Math.Abs(p.Longitude - q.Longitude) * 69_500 < 15)) / (double)line.Count;
        foreach (var (name, start, km) in Starts)
        {
            foreach (var lean in new[] { -1, -0.5, 0.5, 1 })
            {
                // A Request of its own for each Lean, so one Lean's search doesn't feed the next's.
                var centre = await planner.PlanAsync(start, km);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var reRanked = await centre.ReRankAsync(new WendYourWay.Core.Loops.Leans(Hills: lean));
                var took = clock.Elapsed.TotalSeconds;
                var fresh = await planner.PlanAsync(start, km, new WendYourWay.Core.Loops.Leans(Hills: lean));
                var shared = reRanked.Count(r => fresh.Any(f => Alike(f, r)));
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                    $"RERANK {name,-17} {km,3:0} km lean {lean,4:+0.0;-0.0}: {took:0.000} s, {reRanked.Count} Loops ({shared} of the new search's {fresh.Count}); Loop 1 re-ranked {Of(reRanked[0])}, searched {Of(fresh[0])}, centre {Of(centre[0])}"));

                // Then the background search (T053b).
                clock.Restart();
                var searched = await centre.SearchAsync(new WendYourWay.Core.Loops.Leans(Hills: lean));
                var searching = clock.Elapsed.TotalSeconds;
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                    $"SEARCH {name,-17} {km,3:0} km lean {lean,4:+0.0;-0.0}: {searching:0.00} s, {searched.Count} Loops ({searched.Count(r => fresh.Any(f => Alike(f, r)))} alike the new search's {fresh.Count}); Loop 1 {Of(searched[0])}, the new search's {Of(fresh[0])}"));
            }
        }
    }

    //  PLAN T054: streaming, from the public starts and the longer Requests: when the first Loop is
    //  shown, when the last, and how often it reports.
    [Test]
    [Explicit("A measurement of streaming")]
    public async Task Streaming_from_each_start()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var terrain = RealNetwork.Terrain();
        if (terrain is null) Assert.Ignore("No OS Terrain 50 tiles: set WEND_TERRAIN.");
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default, terrain);
        (string, LatLon, double)[] longer =
        [
            ("kenwood-house", new LatLon(51.5712, -0.1677), 20), ("kenwood-house", new LatLon(51.5712, -0.1677), 40),
            ("box-hill", new LatLon(51.2510, -0.3127), 40), ("wanstead-flats", new LatLon(51.5530, 0.0270), 20),
        ];
        foreach (var (name, start, km) in Starts.Concat(longer))
        {
            await planner.PlanAsync(start, km);   // warm, as the app is after its first Request
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double? firstLoop = null, counted = null;
            var (reports, offers) = (0, 0);
            IOfferedLoops? last = null;
            var offered = await planner.PlanAsync(start, km, progress: p =>
            {
                reports++;
                if (p.Stage == PlanningStage.Reading && p.Total is not null) counted ??= clock.Elapsed.TotalSeconds;
                if (p.SoFar is { Count: > 0 } soFar && soFar != last)
                {
                    (last, offers) = (soFar, offers + 1);
                    firstLoop ??= clock.Elapsed.TotalSeconds;
                }
            });
            var done = clock.Elapsed.TotalSeconds;
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                $"STREAM {name,-17} {km,3:0} km: first Loop {firstLoop:0.0} s, all {done:0.0} s; counted at {counted:0.0} s; {reports} reports, {offers} offers; Loop 1 {offered[0].Breakdown.LengthM / 1000:0.0} km green {offered[0].Breakdown.OffRoadPercent + offered[0].Breakdown.TrafficFreePercent:0}% Main Road {offered[0].Breakdown.MainRoadLengthM:0} m"));
        }
    }

    //  PLAN T054: how believable "about N s left" is: each report's time left against what was left
    //  of its stage, and how long the meter sits full while the slice is assembled.
    [Test]
    [Explicit("A measurement of the time left")]
    public async Task Time_left_against_time_taken()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default, RealNetwork.Terrain());
        (string, LatLon, double)[] requests =
        [
            ("kenwood-house", new LatLon(51.5712, -0.1677), 20), ("kenwood-house", new LatLon(51.5712, -0.1677), 40),
            ("box-hill", new LatLon(51.2510, -0.3127), 40),
        ];
        foreach (var (name, start, km) in requests)
        {
            foreach (var warm in new[] { false, true })
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var seen = new List<(double At, PlanningProgress Progress)>();
                await planner.PlanAsync(start, km, progress: p => seen.Add((clock.Elapsed.TotalSeconds, p)));
                foreach (var stage in new[] { PlanningStage.Reading, PlanningStage.Trying })
                {
                    var reports = seen.Where(s => s.Progress.Stage == stage).ToList();
                    if (reports.Count == 0) continue;
                    var end = reports[^1].At;
                    var ratios = reports.Where(r => r.Progress.Left is { } l && l > TimeSpan.Zero && end - r.At > 0.5)
                        .Select(r => r.Progress.Left!.Value.TotalSeconds / (end - r.At)).ToList();
                    var full = stage == PlanningStage.Reading && reports.FirstOrDefault(r => r.Progress.Done == r.Progress.Total) is { Progress: not null } first
                        ? end - first.At : 0;
                    await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                        $"LEFT {name,-15} {km,3:0} km {(warm ? "warm" : "cold")} {stage,-8}: {end - reports[0].At:0.0} s, said/actual {(ratios.Count == 0 ? "-" : string.Join(" ", ratios.Select(r => r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))))}") +
                        (stage == PlanningStage.Reading ? FormattableString.Invariant($"; full for {full:0.0} s") : ""));
                }
            }
        }
    }

    //  Before an Off-road Lean (PLAN T058): from the public starts, how Loop 1 splits between
    //  Off-road, Traffic-free and road.
    [Test]
    [Explicit("A measurement of Off-road against Traffic-free")]
    public async Task Off_road_and_Traffic_free_from_each_start()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default);
        foreach (var (name, start, km) in Starts)
        {
            await planner.PlanAsync(start, km);   // warm
            foreach (var lean in new[] { -1, -0.5, 0, 0.5, 1 })
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                if (await planner.PlanAsync(start, km, new WendYourWay.Core.Loops.Leans(OffRoad: lean)) is not [var best, ..]) continue;
                var took = clock.Elapsed.TotalSeconds;
                var b = best.Breakdown;
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                    $"SPLIT {name + "-" + km,-26} {lean,4:+0.0;-0.0;0}: Off-road {b.OffRoadPercent:0}% Traffic-free {b.TrafficFreePercent:0}% Main Road {b.MainRoadLengthM:0} m {b.LengthM / 1000:0.0} km · {took:0.0} s"));
            }
        }
    }

    //  Before a Novelty Lean (PLAN T059): from the public starts, how much of Loop 1 is on ground
    //  the user's runs, walks and hikes already covered.
    [Test]
    [Explicit("A measurement of familiar ground")]
    public async Task Familiar_ground_from_each_start()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var (slices, geometry) = (new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource));
        var covered = await FamiliarGround.RunAsync(dataSource);
        foreach (var (name, start, km) in Starts)
        {
            var request = new WendYourWay.Core.Loops.Request(await geometry.ToGridAsync(start), km);
            var slice = await slices.AroundAsync(request.Start, request.SliceRadiusM);
            if (WendYourWay.Core.Loops.LoopGenerator.Generate(slice, request, ScoringWeights.Default) is not { } loop) continue;
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                $"FAMILIAR {name + "-" + km,-26} Loop 1 {FamiliarGround.Share(slice, loop, covered):P0} familiar"));
        }
    }

    //  T062: Loop 1 from each of the best car parks in a view, one at a time, as comparing them
    //  before committing would: how long each takes, and the memory kept after.
    [Test]
    [Explicit("A measurement of comparing car parks")]
    public async Task Loop_1_from_the_best_car_parks_in_view()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var terrain = RealNetwork.Terrain();
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default, terrain);
        (string Name, MapBounds View)[] views =
        [
            ("richmond-park", new MapBounds(51.425, -0.32, 51.465, -0.23)),
            ("box-hill", new MapBounds(51.23, -0.36, 51.28, -0.26)),
            ("epping-forest", new MapBounds(51.62, -0.02, 51.69, 0.10)),
        ];
        foreach (var (name, view) in views)
        {
            foreach (var km in new[] { 10.0, 20 })
            {
                var best = (await new PostgisCarParks(dataSource).AroundAsync(view, km)).Best.Take(5).ToList();
                var before = GC.GetTotalMemory(forceFullCollection: true);
                var times = new List<string>();
                foreach (var carPark in best)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var offered = await planner.PlanAsync(carPark.At, km);
                    times.Add(FormattableString.Invariant($"{carPark.Name} {clock.Elapsed.TotalSeconds:0.0} s{(offered.Count > 0 ? FormattableString.Invariant($" ({offered[0].Breakdown.LengthM / 1000:0.0} km, {offered[0].Breakdown.OffRoadPercent + offered[0].Breakdown.TrafficFreePercent:0}% green)") : " (none)")}"));
                }
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant($"COMPARE {name} {km:0} km: ") + string.Join(" | ", times) +
                    FormattableString.Invariant($"; {(GC.GetTotalMemory(forceFullCollection: true) - before) / 1e6:0} MB kept"));
            }
        }
    }

    //  PLAN T068: Gravel Ride Distances, 40 to 100 km, with the Run's rules as they are, before a
    //  Gravel Ride's are built: the slice, each phase, and the memory, one Request at a time from
    //  public starts. WEND_LONG_KM picks the Distances ("40,60"), as 100 km may not fit;
    //  WEND_LONG_START one start, so its peak memory is its own in a process of its own;
    //  WEND_ACTIVITY=gravel a Gravel Ride, =trail a Trail Ride.
    [Test]
    [Explicit("A measurement of long Loops")]
    public async Task Long_Loops_for_Gravel_Rides()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var (slices, geometry) = (new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource));
        var distances = (Environment.GetEnvironmentVariable("WEND_LONG_KM") ?? "40,60,100")
            .Split(',').Select(km => double.Parse(km, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        (string Name, LatLon Start)[] starts =
        [
            ("box-hill", new LatLon(51.2510, -0.3127)),
            ("ashridge", new LatLon(51.8155, -0.5860)),
            ("kenwood-house", new LatLon(51.5712, -0.1677)),
            // T021: Trail Rides, from car parks on the Ridgeway and on Cranborne Chase.
            ("barbury-castle", new LatLon(51.4859, -1.7917)),
            ("cranborne-chase", new LatLon(50.9836, -2.1107)),
        ];
        var activity = Environment.GetEnvironmentVariable("WEND_ACTIVITY") switch
        {
            "gravel" => WendYourWay.Core.Loops.Activity.GravelRide,
            "trail" => WendYourWay.Core.Loops.Activity.TrailRide,
            _ => WendYourWay.Core.Loops.Activity.Run,
        };
        var only = Environment.GetEnvironmentVariable("WEND_LONG_START");
        foreach (var km in distances)
        {
            foreach (var (name, start) in starts.Where(s => only is null || s.Name == only))
            {
                GC.Collect();
                var before = GC.GetTotalMemory(forceFullCollection: true);
                var request = new WendYourWay.Core.Loops.Request(await geometry.ToGridAsync(start), km, Activity: activity);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var slice = await slices.AroundAsync(request.Start, request.SliceRadiusM, activity);
                var loading = clock.Elapsed.TotalSeconds;
                var held = (GC.GetTotalMemory(forceFullCollection: true) - before) / 1e6;
                clock.Restart();
                var found = WendYourWay.Core.Loops.LoopGenerator.Find(slice, request, ScoringWeights.Default);
                var finding = clock.Elapsed.TotalSeconds;
                var offered = found.Offer(WendYourWay.Core.Loops.Leans.Centre);
                // How far each Loop reaches from its start, as a share of its length: half is as far as
                // any can; a round one reaches about a third.
                var startPoint = offered.Count > 0 ? slice.Node(offered[0].Start) : request.Start;
                var reach = offered.Select(o => o.Traversals.Max(t => Math.Max(slice.Node(slice.Segments[t.Segment].From).DistanceTo(startPoint), slice.Node(slice.Segments[t.Segment].To).DistanceTo(startPoint)))
                    / o.Traversals.Sum(t => slice.Segments[t.Segment].LengthM)).ToArray();
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                    $"LONG {name,-14} {km,3:0} km: {slice.Segments.Count,9:N0} Segments, load {loading,6:0.0} s, find {finding,6:0.0} s, {offered.Count} Loops ") +
                    string.Join(" | ", offered.Take(3).Select(o => FormattableString.Invariant($"{o.Traversals.Sum(t => slice.Segments[t.Segment].LengthM) / 1000:0.0} km"))) +
                    FormattableString.Invariant($"; slice {held:0} MB; peak {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1e9:0.0} GB; {activity}; reach ") +
                    string.Join(" ", reach.Select(r => FormattableString.Invariant($"{r:0.00}"))));
                GC.KeepAlive(slice);
            }
        }
    }

    //  PLAN T068: the Loops offered from a slice of 0.4 of the longest Loop against half of it, at
    //  40 and 60 km from the three starts: the same Segments in the same order, or not.
    [Test]
    [Explicit("A measurement of the smaller slice")]
    public async Task The_smaller_slice_offers_the_same_Loops()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var (slices, geometry) = (new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource));
        (string Name, LatLon Start)[] starts =
        [
            ("box-hill", new LatLon(51.2510, -0.3127)),
            ("ashridge", new LatLon(51.8155, -0.5860)),
            ("kenwood-house", new LatLon(51.5712, -0.1677)),
        ];
        foreach (var km in new[] { 40.0, 60.0 })
        {
            foreach (var (name, start) in starts)
            {
                var request = new WendYourWay.Core.Loops.Request(await geometry.ToGridAsync(start), km);
                var half = (request.LongestKm * 1000 / 2) + WendYourWay.Core.Loops.Request.SnapMarginM;
                var before = await Offered(half);
                GC.Collect();
                var after = await Offered(request.SliceRadiusM);
                GC.Collect();
                var same = before.Count == after.Count && before.Zip(after).All(p => p.First.Keys.SequenceEqual(p.Second.Keys));
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                    $"SAME {name,-14} {km,3:0} km: radius {half / 1000:0.0} to {request.SliceRadiusM / 1000:0.0} km, {before.Count} and {after.Count} Loops, {(same ? "the same" : "DIFFERENT")}"));
                if (!same)
                {
                    // Each Loop either way: its score, length and how much of it the other side's Loop at that place shares.
                    for (var i = 0; i < Math.Max(before.Count, after.Count); i++)
                    {
                        var b = i < before.Count ? before[i] : default;
                        var a = i < after.Count ? after[i] : default;
                        var shared = b.Keys is null || a.Keys is null ? 0 : b.Keys.Intersect(a.Keys).Count() / (double)Math.Max(b.Keys.Length, a.Keys.Length);
                        await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                            $"  Loop {i + 1}: half {b.Score:0.000} {b.Km:0.0} km | 0.4 {a.Score:0.000} {a.Km:0.0} km | shared {shared:P0}; in either's offer: {(b.Keys is null ? "-" : after.Any(x => x.Keys.SequenceEqual(b.Keys)) ? "yes" : "no")}"));
                    }
                }

                async Task<List<(WendYourWay.Core.Network.SegmentKey[] Keys, double Score, double Km)>> Offered(double radiusM)
                {
                    var slice = await slices.AroundAsync(request.Start, radiusM);
                    var offered = WendYourWay.Core.Loops.LoopGenerator.Find(slice, request, ScoringWeights.Default).Offer(WendYourWay.Core.Loops.Leans.Centre);
                    return [.. offered.Select(o => (o.Traversals.Select(t => slice.Segments[t.Segment].Key).ToArray(),
                        WendYourWay.Core.Scoring.LoopScorer.Score(slice, o, ScoringWeights.Default),
                        o.Traversals.Sum(t => slice.Segments[t.Segment].LengthM) / 1000))];
                }
            }
        }
    }

    //  PLAN T054: where a Request's time goes, by phase, for longer Loops.
    [Test]
    [Explicit("A measurement of the phases")]
    public async Task Phases_of_a_Request()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var (slices, geometry) = (new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource));
        (string, LatLon, double)[] requests =
        [
            ("kenwood-house", new LatLon(51.5712, -0.1677), 10), ("kenwood-house", new LatLon(51.5712, -0.1677), 20),
            ("kenwood-house", new LatLon(51.5712, -0.1677), 40), ("box-hill", new LatLon(51.2510, -0.3127), 40),
            ("wanstead-flats", new LatLon(51.5530, 0.0270), 20),
        ];
        foreach (var (name, start, km) in requests)
        {
            var request = new WendYourWay.Core.Loops.Request(await geometry.ToGridAsync(start), km);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var slice = await slices.AroundAsync(request.Start, request.SliceRadiusM);
            var loading = clock.Elapsed.TotalSeconds;
            clock.Restart();
            _ = slice.OffRoadNetworks;
            var networks = clock.Elapsed.TotalSeconds;
            var junction = Enumerable.Range(0, slice.NodeCount).MinBy(n => slice.Node(n).DistanceTo(request.Start));
            clock.Restart();
            var destinations = WendYourWay.Core.Loops.DestinationLoops.Candidates(slice, junction, km * 1000, ScoringWeights.Default);
            var destining = clock.Elapsed.TotalSeconds;
            clock.Restart();
            var found = WendYourWay.Core.Loops.LoopGenerator.Find(slice, request, ScoringWeights.Default);
            var finding = clock.Elapsed.TotalSeconds;
            clock.Restart();
            found.Offer(WendYourWay.Core.Loops.Leans.Centre);
            var choosing = clock.Elapsed.TotalSeconds;
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                $"PHASES {name,-15} {km,3:0} km: {slice.Segments.Count,7} Segments; load {loading:0.00} s, Off-road networks {networks:0.00} s, destination Loops {destining:0.00} s ({destinations.Count}), all of Find {finding:0.00} s ({found.Count} in the window), choosing {choosing:0.00} s"));
        }
    }

    //  PLAN T055: where reading a slice's time goes: the query as it is, without the Segments
    //  beside roads, with the bare columns only, and its plan.
    [Test]
    [Explicit("A measurement of reading a slice")]
    public async Task Reading_a_slice_by_parts()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var geometry = new MapGeometry(dataSource);
        var request = new WendYourWay.Core.Loops.Request(await geometry.ToGridAsync(new LatLon(51.5712, -0.1677)), 40);
        const string within = " where ST_DWithin(s.geom, ST_SetSRID(ST_MakePoint(@x, @y), 27700), @radius)";
        const string beside = """
             left join lateral (
               select array_agg(r.osm_way_id) as ways, array_agg(r.from_node_id) as froms,
                      array_agg(r.to_node_id) as tos, array_agg(b.metres) as metres
                 from network.segment_beside b
                 join network.segments r on r.id = b.road_id
                where b.path_id = s.id) beside on true
            """;
        const string columns = """
            select s.osm_way_id, s.from_node_id, s.to_node_id,
                   ST_X(ST_StartPoint(s.geom)), ST_Y(ST_StartPoint(s.geom)),
                   ST_X(ST_EndPoint(s.geom)), ST_Y(ST_EndPoint(s.geom)),
                   s.length_m, s.ground::text, s.access_run::text,
                   s.main_road, s.greenery, s.corridor, s.waterside,
                   s.crossing, s.crossing_main_road, s.crossing_share, s.bridge_over_water, s.ascent_m, s.descent_m
            """;
        foreach (var (what, sql) in new[]
        {
            ("as it is", columns.Replace("s.main_road,", "beside.ways, beside.froms, beside.tos, beside.metres, s.main_road,", StringComparison.Ordinal) + " from network.segments s" + beside + within),
            ("no beside", columns + " from network.segments s" + within),
            ("ids only", "select s.osm_way_id from network.segments s" + within),
            ("beside alone", "select b.path_id, r.osm_way_id, b.metres from network.segment_beside b join network.segments r on r.id = b.road_id join network.segments s on s.id = b.path_id" + within),
            ("beside by id", "select b.path_id, b.road_id, b.metres from network.segment_beside b where b.path_id in (select s.id from network.segments s" + within + ")"),
            ("all beside", "select b.path_id, b.road_id, b.metres from network.segment_beside b"),
            ("with ids", columns.Replace("select s.osm_way_id,", "select s.id, s.osm_way_id,", StringComparison.Ordinal) + " from network.segments s" + within),
        })
        {
            for (var run = 0; run < 2; run++)
            {
                await using var query = dataSource.CreateCommand(sql);
                query.Parameters.Add(new NpgsqlParameter("x", request.Start.X));
                query.Parameters.Add(new NpgsqlParameter("y", request.Start.Y));
                query.Parameters.Add(new NpgsqlParameter("radius", request.SliceRadiusM));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var rows = 0;
                await using (var reader = await query.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        for (var c = 0; c < reader.FieldCount; c++) _ = reader.GetValue(c);
                        rows++;
                    }
                }
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant($"READ {what,-13} run {run + 1}: {rows} rows in {clock.Elapsed.TotalSeconds:0.00} s"));
            }
        }
        await using var explain = dataSource.CreateCommand("explain (analyze, buffers) select b.path_id, b.road_id, b.metres from network.segment_beside b where b.path_id in (select s.id from network.segments s" + within + ")");
        explain.Parameters.Add(new NpgsqlParameter("x", request.Start.X));
        explain.Parameters.Add(new NpgsqlParameter("y", request.Start.Y));
        explain.Parameters.Add(new NpgsqlParameter("radius", request.SliceRadiusM));
        await using var plan = await explain.ExecuteReaderAsync();
        while (await plan.ReadAsync()) await TestContext.Out.WriteLineAsync("PLAN " + plan.GetString(0));
    }

    //  PLAN T054: how to estimate a slice's Segments before reading it, for "n of about N".
    [Test]
    [Explicit("A measurement of estimates")]
    public async Task Estimating_a_slice()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var geometry = new MapGeometry(dataSource);
        (string, LatLon, double)[] requests =
        [
            ("kenwood-house", new LatLon(51.5712, -0.1677), 10), ("kenwood-house", new LatLon(51.5712, -0.1677), 40),
            ("box-hill", new LatLon(51.2510, -0.3127), 40), ("wanstead-flats", new LatLon(51.5530, 0.0270), 20),
            ("greenwich-park", new LatLon(51.4769, 0.0005), 10),
        ];
        foreach (var (name, start, km) in requests)
        {
            var request = new WendYourWay.Core.Loops.Request(await geometry.ToGridAsync(start), km);
            var line = FormattableString.Invariant($"ESTIMATE {name,-15} {km,3:0} km:");
            foreach (var (what, sql) in new[]
            {
                ("exact", "select count(*) from network.segments s"),
                ("system 1%", "select 100 * count(*) from network.segments s tablesample system (1) repeatable (1)"),
                ("bernoulli 1%", "select 100 * count(*) from network.segments s tablesample bernoulli (1) repeatable (1)"),
            })
            {
                await using var query = dataSource.CreateCommand(sql + " where ST_DWithin(s.geom, ST_SetSRID(ST_MakePoint(@x, @y), 27700), @radius)");
                query.Parameters.Add(new NpgsqlParameter("x", request.Start.X));
                query.Parameters.Add(new NpgsqlParameter("y", request.Start.Y));
                query.Parameters.Add(new NpgsqlParameter("radius", request.SliceRadiusM));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var count = Convert.ToInt64(await query.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
                line += FormattableString.Invariant($" {what} {count} in {clock.Elapsed.TotalSeconds:0.00} s;");
            }
            await TestContext.Out.WriteLineAsync(line);
        }
    }

    //  PLAN T053: what the Loops offered keep in memory (their slice and the Loops found) while the
    //  page holds them, for the largest Requests. Synchronous, so no async machinery holds them.
    [Test]
    [Explicit("A measurement of memory")]
    public void Memory_kept_by_the_Loops_offered()
    {
        var dataSource = RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain).GetAwaiter().GetResult();
        try
        {
            // No terrain: its tiles stay cached whatever the Loops offered keep.
            var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default);
            Measure("plain 100 MB", () => new byte[100_000_000]);
            foreach (var km in new[] { 10, 40 })
                Measure(FormattableString.Invariant($"box-hill {km} km"), () => planner.PlanAsync(new LatLon(51.2510, -0.3127), km).GetAwaiter().GetResult());
        }
        finally
        {
            dataSource.Dispose();
        }
    }

    private static void Measure(string what, Func<object> make)
    {
        var before = Settled();
        var (held, kept) = Hold(make);
        var dropped = Settled();
        TestContext.Out.WriteLine(FormattableString.Invariant(
            $"MEMORY {what}: {(held - before) / 1e6:0} MB held, {(dropped - before) / 1e6:0} MB once dropped (gone: {!kept.IsAlive})"));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (long Held, WeakReference Kept) Hold(Func<object> make)
    {
        var made = make();
        return (Settled(), new WeakReference(made));
    }

    private static long Settled()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
