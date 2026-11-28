using System.Globalization;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>A rule a route is chosen by (T200): its name, its size, what it says, and when it doesn't hold.</summary>
/// <param name="Name">What it's about, in CONTEXT.md's words.</param>
/// <param name="Figure">Its size, as a person can weigh it: "3×" an Off-road metre, "3,000 m" of Off-road.</param>
/// <param name="Says">What puts a way under it, or what it does, in plain words; empty if its name says it.</param>
/// <param name="Except">Its exceptions, each in plain words; none if empty.</param>
public sealed record Rule(string Name, string Figure, string Says, IReadOnlyList<string> Except);

/// <summary>Rules of one kind (T200), in the order they weigh.</summary>
public sealed record RuleGroup(string Heading, string Intro, IReadOnlyList<Rule> Rules);

/// <summary>
/// The rules a route is chosen by (T200, #204; STORY-053), read from the weights the planner uses and the
/// <see cref="GroundRules"/> the Network is built by, so the list can't go out of date: each ground's rate, cheapest
/// first, with what puts a way on it and the exceptions; what makes a metre cheaper or dearer; one-off costs, dearest
/// first; a Gravel Ride's Obstacles; and how whole Loops are ranked once found.
/// </summary>
public static class Rules
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    private static string Times(double x) => string.Create(En, $"{x:0.##}×");

    private static string Metres(double m) => string.Create(En, $"{m:#,0} m");

    private static string Percent(double share) => string.Create(En, $"{share * 100:0.#}%");

    private static string Number(double x) => string.Create(En, $"{x:0.####}");

    /// <summary>The rules under these weights, a Run's unless said.</summary>
    public static IReadOnlyList<RuleGroup> Of(ScoringWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        return [Ground(weights), Adjustments(weights), OneOffs(weights), Obstacles(), Ranking(weights)];
    }

    // Each ground's rate against an Off-road metre, cheapest first.
    private static RuleGroup Ground(ScoringWeights weights)
    {
        var offRoad = weights.OffRoadPerMetre;
        var beside = string.Create(En,
            $"A path is beside a road when {Percent(GroundRules.MostlyShare)} of it lies within {GroundRules.BesideM:0} m of the road's line, running the same way; " +
            $"within {GroundRules.BesideBigRoadM:0} m of a dual carriageway or a road of 4 or more lanes.");
        (double Rate, Rule Rule)[] ground =
        [
            (weights.OffRoadPerMetre, new("Off-road", Times(1),
                "Paths, tracks and bridleways that aren't paved: dirt, grass, gravel and the like, or with no surface on the map.",
                [
                    "A public footpath drawn as a footway with no surface on the map is taken as unpaved: most cross fields.",
                    string.Create(En, $"A dirt path mostly beside roads tagged {GroundRules.FastMph} mph or more (or the national limit) is beside traffic: a Connector. ") +
                        "Beside slower roads, or ones with no speed on the map, it stays Off-road.",
                    "A path with no surface on the map mostly beside a road is a Connector: beside a road it's as likely tarmac.",
                ])),
            (weights.TrafficFreePerMetre, new("Traffic-free", Times(weights.TrafficFreePerMetre / offRoad),
                "Paved ways away from traffic: tarmac paths, footways, cycleways, alleys, and roads closed to motor traffic with no buses.",
                ["A paved path mostly beside a road is a Connector."])),
            (weights.ConnectorPerMetre, new("Connector", Times(weights.ConnectorPerMetre / offRoad),
                "Roads with traffic, and the pavements and paved paths beside them. " + beside,
                [
                    "A pavement drawn on its own is a Connector, even with no road drawn beside it.",
                    "A private road still carries residents' cars: a Connector.",
                ])),
            (weights.PavedMainRoadCostPerMetre, new("Main Road with pavements", Times(weights.PavedMainRoadCostPerMetre / offRoad),
                string.Create(En, $"On a Run, a Main Road of {GroundRules.PavementMph} mph or under with pavements on both sides, and the pavements beside one: ") +
                    "a short hop beside a park beats a long way round.",
                ["On a ride it's the road: a Main Road."])),
            (weights.MainRoadCostPerMetre, new("Main Road", Times(weights.MainRoadCostPerMetre / offRoad),
                "Heavy traffic: motorways, trunk, primary and secondary roads, and the paths mostly beside them. Taken only for a short link, never because it's beside green.",
                ["A Main Road's pavement mostly beside water is the river's ground, not the road's: costed as a path."])),
        ];
        return new("What a metre of ground costs", "Each against a metre of Off-road, cheapest first: a route is found the cheapest way round.",
            [.. ground.OrderBy(g => g.Rate).Select(g => g.Rule)]);
    }

    // What makes a metre cheaper or dearer than its ground's rate.
    private static RuleGroup Adjustments(ScoringWeights weights) => new("What makes a metre cheaper or dearer", "On top of its ground's rate.",
    [
        new("Covering ground again", Times(weights.ReusedGroundMultiplier),
            "A metre the Loop has already used costs this many times its rate. Back along the other pavement of the same road, or a path a few metres off, is the same ground.", []),
        new("Greenery", "up to −" + Percent(weights.GreeneryDiscount),
            "Cheaper by how much of it lies beside park, wood, common or water.", ["Never on a Main Road: green beside it doesn't make it better."]),
        new("Corridor", "a further −" + Percent(weights.CorridorDiscount),
            "On a towpath, a disused railway or a named walking route.", ["A road on a walking route is just a road."]),
        new("Off-road Lean", $"{Times(1 + weights.OffRoadLeanWeight)} to {Times(1 - weights.TrailsLeanWeight)}",
            "Leaning to tarmac, an Off-road metre costs up to the first; leaning to trails, as little as the second.",
            ["Never dearer than an ordinary road, so roads stay behind green ground."]),
        new("Hills Lean", $"up to {Times(1 + (weights.FewerHillsCostWeight * RunCostModel.SteepestRelief))} or {Times(1 + weights.MoreHillsCostWeight)}",
            "Leaning to fewer hills, the steepest ground (1 in 10) costs up to the first; leaning to more, flat ground costs up to the second.",
            ["Never enough to make a road cheaper than a green path."]),
    ]);

    // Costs met once, in metres of Off-road, dearest first.
    private static RuleGroup OneOffs(ScoringWeights weights)
    {
        var c = weights.Crossings ?? CrossingCosts.Default;
        (double M, Rule Rule)[] once =
        [
            (c.BusyUnmarked, new("Crossing a busy road with no crossing", Metres(c.BusyUnmarked),
                "A trunk road, a road of 4 or more lanes, or one side of a dual carriageway: never chosen while there's another way.", [])),
            (c.MainRoadUnmarked, new("Crossing a Main Road with no crossing", Metres(c.MainRoadUnmarked),
                "A walk along the road to a crossing comes cheaper.", [])),
            (c.MainRoadSignals, new("Crossing a Main Road at lights", Metres(c.MainRoadSignals), "Waiting at lights costs.", [])),
            (c.MainRoadMarked, new("Crossing a Main Road at a zebra or marked crossing", Metres(c.MainRoadMarked), "", [])),
            (c.QuietSignals, new("Crossing a quiet road at lights", Metres(c.QuietSignals), "", [])),
            (c.QuietUnmarked, new("Crossing a quiet road", Metres(c.QuietUnmarked), "",
                ["A crossing drawn with no kind on the map is taken as marked: someone thought it one.", "A footbridge or underpass isn't a crossing: nothing extra."])),
            (RunCostModel.GapM, new("A gap in the map", Metres(RunCostModel.GapM), "Taken only for a real gain, not to cut a corner.", [])),
            (weights.CustomersM, new("A way for customers only", Metres(weights.CustomersM), "A gate's worth: taken where it's the natural way, never sought out.", [])),
        ];
        return new("What a moment costs", "Each as the metres of Off-road it's worth going round to avoid, dearest first.",
            [.. once.OrderByDescending(o => o.M).Select(o => o.Rule)]);
    }

    // A Gravel Ride's Obstacles, dearest first; a Run pays for none.
    private static RuleGroup Obstacles()
    {
        var o = ObstacleCosts.GravelRide;
        (string Name, double M)[] kinds =
            [("Stile", o.Stile), ("Kissing gate", o.KissingGate), ("Turnstile", o.Turnstile), ("Motorcycle barrier", o.MotorcycleBarrier),
             ("Ford", o.Ford), ("Cycle barrier", o.CycleBarrier), ("Gate", o.Gate), ("Cattle grid", o.CattleGrid)];
        return new("Obstacles, on a Gravel Ride", "Each as metres of Off-road, dearest first. On a Run they cost nothing.",
            [.. kinds.OrderByDescending(k => k.M).Select(k => new Rule(k.Name, Metres(k.M), "", []))]);
    }

    // Once found, Loops are scored whole to rank them: gentler on some things than the search.
    private static RuleGroup Ranking(ScoringWeights weights)
    {
        var run = weights.Ranking(Loops.Activity.Run);
        return new("How whole Loops are ranked", "Once found, each Loop is scored as a whole, as you'd judge it, and the best come first.",
        [
            new("Main Road", $"{Times(weights.LoopMainRoadPerMetre / weights.OffRoadPerMetre)}, not {Times(weights.MainRoadCostPerMetre / weights.OffRoadPerMetre)}",
                "A Loop's Main Road link is judged as an ordinary road's: a good Loop may take one to reach somewhere worth it.", []),
            new("Covering ground again", $"{Times(weights.LoopReusedGroundMultiplier)}, not {Times(weights.ReusedGroundMultiplier)}",
                "Out and back along a canal can still be a good Loop.", []),
            new("Flow", "up to +" + Percent(weights.FlowWeight), "Raised by its share in long green stretches.", []),
            new("Water", "up to +" + Percent(weights.WaterWeight), "Raised by its share beside water.", []),
            new("Knots", "−" + Percent(weights.KnotPenalty) + " each", "Each place it tangles round on itself.", []),
            new("Off-road, on a Run", "+" + Number(run.OffRoadShareWeight) + " for one wholly off-road", "As your stars weigh it, on top of the score.", []),
            new("Climbing, on a Run", "+" + Number(run.ClimbPerKmWeight) + " a metre climbed a km", "As your stars weigh it, on top of the score.", []),
        ]);
    }
}
