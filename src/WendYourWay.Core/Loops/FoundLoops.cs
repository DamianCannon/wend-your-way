using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Loops;

/// <summary>
/// Every Loop a Request found in its length window (T053), from which the Loops offered are chosen,
/// and chosen again when a Lean moves, with no new search (R10.4, ADR 0007).
/// </summary>
public sealed class FoundLoops
{
    private readonly NetworkSlice _slice;
    private readonly ScoringWeights _weights;
    private readonly Loop[] _loops;

    // What doesn't depend on the Lean, worked out the first time a choice needs it: most
    // candidates are turned away on their heading alone.
    private readonly double?[] _headings;
    private readonly bool[] _headed;
    private readonly IReadOnlySet<int>?[] _covered;
    private readonly double?[] _mainRoadM;

    internal FoundLoops(NetworkSlice slice, ScoringWeights weights, Loop[] loops)
    {
        (_slice, _weights, _loops) = (slice, weights, loops);
        _headings = new double?[loops.Length];
        _headed = new bool[loops.Length];
        _covered = new IReadOnlySet<int>?[loops.Length];
        _mainRoadM = new double?[loops.Length];
    }

    /// <summary>How many Loops were found.</summary>
    public int Count => _loops.Length;

    /// <summary>
    /// These Loops and <paramref name="more"/>, found on the same slice at other Leans (a
    /// background search, T053b), to choose from together.
    /// </summary>
    /// <exception cref="ArgumentException">They were found on another slice.</exception>
    public FoundLoops With(FoundLoops more)
    {
        ArgumentNullException.ThrowIfNull(more);
        if (more._slice != _slice) throw new ArgumentException("Loops found on another slice can't be chosen from with these.", nameof(more));
        return new FoundLoops(_slice, _weights, [.. _loops, .. more._loops]);
    }

    /// <summary>
    /// The Loops offered at these Leans, best first (R1.4): the best by its score,
    /// then each next best that's distinct from those already offered, heads another way
    /// ("different directions", user) and is good enough. Being different is no reason to offer a
    /// poor Loop, nor one along Main Roads. While fewer than five are on that bar, the next best
    /// heading where none offered goes are added after them, below it but not far (T096).
    /// </summary>
    public LoopsOffered Offer(Leans leans)
    {
        ArgumentNullException.ThrowIfNull(leans);
        var weights = leans.On(_weights);
        // Requests can overlap (a new one starts as the last is cancelled): one choice at a time.
        lock (_loops)
        {
            // Best per-km score first; among equals, the first found (a stable sort).
            var ranked = Enumerable.Range(0, _loops.Length)
                .Select(i => (Index: i, Score: LoopScorer.Score(_slice, _loops[i], weights)))
                .OrderByDescending(c => c.Score)
                .ToArray();

            var offered = new List<int>();
            double firstScore = 0, firstMainRoadM = 0;
            foreach (var (i, score) in ranked)
            {
                if (offered.Count == LoopGenerator.Offered || (offered.Count > 0 && score < LoopGenerator.LeastScoreShare * firstScore)) break;
                if (offered.Count == 0) (firstScore, firstMainRoadM) = (score, MainRoadOf(i));
                else if (!Adds(offered, i, firstMainRoadM + LoopGenerator.MainRoadAllowanceM)) continue;
                offered.Add(i);
            }

            var onTheBar = offered.Count;
            foreach (var (i, score) in ranked)
            {
                if (offered.Count is 0 or LoopGenerator.Offered || score < LoopGenerator.LeastScoreShareInAnotherDirection * firstScore) break;
                // One already offered heads its own way, so is never added again.
                if (!Adds(offered, i, firstMainRoadM + LoopGenerator.MainRoadAllowanceInAnotherDirectionM)) continue;
                offered.Add(i);
            }
            return new LoopsOffered([.. offered.Select(i => _loops[i])], onTheBar);
        }
    }

    // Whether Loop i adds to those offered: it heads another way from each, shares little ground
    // with any, and takes no more Main Road than allowed.
    private bool Adds(List<int> offered, int i, double mostMainRoadM) =>
        // The cheapest test first: most candidates head the same way as one already offered.
        offered.All(o => HeadsAway(HeadingOf(o), HeadingOf(i)))
        && offered.All(o => SharedGround.Share(_slice, _loops[o], CoveredBy(o), _loops[i], CoveredBy(i)) <= LoopGenerator.MostShared)
        && MainRoadOf(i) <= mostMainRoadM;

    private double? HeadingOf(int i)
    {
        if (!_headed[i]) (_headings[i], _headed[i]) = (LoopHeading.Of(_slice, _loops[i]), true);
        return _headings[i];
    }

    private IReadOnlySet<int> CoveredBy(int i) => _covered[i] ??= SharedGround.Covered(_slice, _loops[i]);

    private double MainRoadOf(int i) => _mainRoadM[i] ??= Breakdown.Of(_slice, _loops[i]).MainRoadLengthM;

    // Two Loops head different ways if their headings are far enough apart; two that go round
    // their start don't.
    private static bool HeadsAway(double? a, double? b)
    {
        if (a is not { } one || b is not { } other) return a is not null || b is not null;
        var apart = Math.Abs(one - other) % 360;
        return Math.Min(apart, 360 - apart) >= LoopGenerator.LeastAngleApart;
    }
}
