using System.Globalization;
using WendYourWay.Data;

namespace WendYourWay.Web.Planning;

/// <summary>
/// The user's own settings for the planner. Home is where Requests start before any other start
/// is chosen (R1.1a); it lives in `dotnet user-secrets` (Home:Latitude, Home:Longitude), never in git.
/// </summary>
public sealed record PlannerSettings(LatLon? Home)
{
    public static PlannerSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return double.TryParse(configuration["Home:Latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && double.TryParse(configuration["Home:Longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            ? new PlannerSettings(new LatLon(latitude, longitude))
            : new PlannerSettings(Home: null);
    }
}
