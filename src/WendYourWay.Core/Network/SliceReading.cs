namespace WendYourWay.Core.Network;

/// <summary>How far reading a slice has got (T054).</summary>
/// <param name="Read">The Segments read so far.</param>
/// <param name="Total">How many there are, once counted, or null until then.</param>
public sealed record SliceReading(int Read, int? Total);
