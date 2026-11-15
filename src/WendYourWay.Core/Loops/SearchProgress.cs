namespace WendYourWay.Core.Loops;

/// <summary>How far a search for Loops has got (T054), reported as each Loop is tried.</summary>
/// <param name="Tried">How many Loops it has tried.</param>
/// <param name="ToTry">How many it will try in all, as far as it knows yet.</param>
/// <param name="Latest">The Loop just tried, or none if it couldn't be made.</param>
/// <param name="SoFar">The Loops found so far in the length window, to choose from before it ends.</param>
public sealed record SearchProgress(int Tried, int ToTry, Loop? Latest, FoundLoops SoFar);
