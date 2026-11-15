namespace WendYourWay.Core.Network;

/// <summary>Where the Network is kept; loads the part of it a Request needs (ADR 0006).</summary>
public interface INetworkSlices
{
    /// <summary>
    /// Every Segment with any part within <paramref name="radiusM"/> metres of
    /// <paramref name="centre"/> (British National Grid), assembled into a slice. How far it has
    /// got goes to <paramref name="reading"/> as it reads, one report at a time (T054). Read for
    /// <paramref name="activity"/>: each Segment's Access is that Activity's, and a Gravel Ride's
    /// slice leaves out what a bike may not use (T068).
    /// </summary>
    Task<NetworkSlice> AroundAsync(NodePoint centre, double radiusM, Loops.Activity activity = Loops.Activity.Run, Action<SliceReading>? reading = null, CancellationToken cancellationToken = default);
}
