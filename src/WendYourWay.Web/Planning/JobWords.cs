using System.Globalization;
using WendYourWay.Data.Building;

namespace WendYourWay.Web.Planning;

/// <summary>How a job is said (T129, T138): what it is, and how long something took.</summary>
public static class JobWords
{
    /// <summary>What a job of this kind is doing, in a few words.</summary>
    public static string What(string kind) => kind switch
    {
        JobKind.Update => "Updating the map",
        JobKind.Import => "Importing runs",
        JobKind.Back => "Changing the map",
        JobKind.Restore => "Restoring a backup",
        _ => "Running",
    };

    /// <summary>"under a minute", "about a minute" is said by the caller; "12 min", "1 h 12 min".</summary>
    public static string Took(TimeSpan took) =>
        took < TimeSpan.FromMinutes(1) ? "under a minute"
        : took < TimeSpan.FromHours(1) ? string.Create(CultureInfo.InvariantCulture, $"{(int)took.TotalMinutes} min")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)took.TotalHours} h {took.Minutes} min");

    /// <summary>About how long: "about a minute" for under one, else "about 3 h 25 min".</summary>
    public static string About(TimeSpan took) => took < TimeSpan.FromMinutes(1.5) ? "about a minute" : "about " + Took(took);
}
