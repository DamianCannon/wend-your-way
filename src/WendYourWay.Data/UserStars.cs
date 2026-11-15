namespace WendYourWay.Data;

/// <summary>The user's own stars for a route (T097, STORY-045): one to five, or none.</summary>
public static class UserStars
{
    /// <summary>Refuses anything but one to five stars, or none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than one star, or more than five.</exception>
    public static void Check(int? stars)
    {
        if (stars is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(stars), stars, "Stars are one to five.");
    }
}
