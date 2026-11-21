using System.Globalization;
using WendYourWay.Data;

namespace WendYourWay.Web.Planning;

/// <summary>
/// The user's own settings for the planner. Home is where Requests start before any other start
/// is chosen (R1.1a); it lives in `dotnet user-secrets` (Home:Latitude, Home:Longitude), never in git.
/// On the practice map (T161, Practice=true), Requests start in Richmond Park whatever Home is set.
/// </summary>
public sealed record PlannerSettings(LatLon? Home)
{
    /// <summary>Whether it's the practice map (T161): nothing done there kept, nothing that changes the map.</summary>
    public bool Practice { get; init; }

    /// <summary>What the Start box calls Home: on the practice map, the place it starts (T161).</summary>
    public string HomeName => Practice ? "Pembroke Lodge" : "Home";

    public static PlannerSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (IsPractice(configuration)) return new PlannerSettings(PracticeMap.Start) { Practice = true };
        return double.TryParse(configuration["Home:Latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && double.TryParse(configuration["Home:Longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            ? new PlannerSettings(new LatLon(latitude, longitude))
            : new PlannerSettings(Home: null);
    }

    /// <summary>Whether the app was opened on the practice map: Practice=true.</summary>
    public static bool IsPractice(IConfiguration configuration) =>
        bool.TryParse(configuration?["Practice"], out var practice) && practice;
}
