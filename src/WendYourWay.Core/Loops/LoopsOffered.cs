using System.Collections;

namespace WendYourWay.Core.Loops;

/// <summary>
/// The Loops a Request offers, best first (R1.4): those on the usual bar, then any in other
/// directions below it, added while fewer than five are on it (T096, STORY-044).
/// </summary>
public sealed class LoopsOffered : IReadOnlyList<Loop>
{
    private readonly IReadOnlyList<Loop> _loops;

    /// <summary>The Loops offered, those on the usual bar first.</summary>
    /// <param name="loops">The Loops, in the order offered.</param>
    /// <param name="onTheBar">How many of them, from the first, are on the usual bar.</param>
    public LoopsOffered(IReadOnlyList<Loop> loops, int onTheBar)
    {
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentOutOfRangeException.ThrowIfNegative(onTheBar);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(onTheBar, loops.Count);
        (_loops, OnTheBar) = (loops, onTheBar);
    }

    /// <summary>How many of the Loops, from the first, are on the usual bar.</summary>
    public int OnTheBar { get; }

    /// <summary>Whether the Loop at <paramref name="index"/> was added in another direction, below the usual bar.</summary>
    public bool InAnotherDirection(int index) => index >= OnTheBar && index < _loops.Count;

    /// <inheritdoc />
    public Loop this[int index] => _loops[index];

    /// <inheritdoc />
    public int Count => _loops.Count;

    /// <inheritdoc />
    public IEnumerator<Loop> GetEnumerator() => _loops.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
