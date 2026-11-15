namespace WendYourWay.Core.Loops;

/// <summary>What a Request is for (CONTEXT.md: Activity): who may go where, and so what a Loop may use (T068).</summary>
public enum Activity
{
    Run,
    GravelRide,

    /// <summary>On a road-legal trail bike: roads and Definitive Map byways (T021, SPEC R9.6).</summary>
    TrailRide,
}
