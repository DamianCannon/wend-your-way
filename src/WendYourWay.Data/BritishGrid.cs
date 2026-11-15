using WendYourWay.Core.Network;

namespace WendYourWay.Data;

/// <summary>
/// British National Grid to latitude and longitude in the app, without the database (T054): the
/// Ordnance Survey's inverse Transverse Mercator on Airy 1830, then its Helmert transformation to
/// WGS84, good to a few metres. For drawing the Loops a search tries; Loops offered go through
/// PostGIS.
/// </summary>
public static class BritishGrid
{
    // Airy 1830 and the National Grid's projection.
    private const double A = 6377563.396, B = 6356256.909, F0 = 0.9996012717;
    private const double N0 = -100000, E0 = 400000;
    private static readonly double Phi0 = Radians(49), Lambda0 = Radians(-2);

    // GRS80 (WGS84) and the Helmert transformation from OSGB36 to it.
    private const double WgsA = 6378137, WgsB = 6356752.3141;
    private const double Tx = 446.448, Ty = -125.157, Tz = 542.060, ScalePpm = -20.4894;
    private static readonly double Rx = Seconds(0.1502), Ry = Seconds(0.2470), Rz = Seconds(0.8421);

    public static LatLon ToLatLon(NodePoint point)
    {
        var (phi, lambda) = OnAiry(point.X, point.Y);

        // To Cartesian on Airy, through the Helmert transformation, back to latitude and longitude on GRS80.
        var e2 = 1 - (B * B / (A * A));
        var nu = A / Math.Sqrt(1 - (e2 * Math.Sin(phi) * Math.Sin(phi)));
        var x = nu * Math.Cos(phi) * Math.Cos(lambda);
        var y = nu * Math.Cos(phi) * Math.Sin(lambda);
        var z = (1 - e2) * nu * Math.Sin(phi);

        var s = 1 + (ScalePpm / 1e6);
        var x2 = Tx + (s * x) - (Rz * y) + (Ry * z);
        var y2 = Ty + (Rz * x) + (s * y) - (Rx * z);
        var z2 = Tz - (Ry * x) + (Rx * y) + (s * z);

        var wgsE2 = 1 - (WgsB * WgsB / (WgsA * WgsA));
        var p = Math.Sqrt((x2 * x2) + (y2 * y2));
        var latitude = Math.Atan2(z2, p * (1 - wgsE2));
        for (var i = 0; i < 10; i++)
        {
            var wgsNu = WgsA / Math.Sqrt(1 - (wgsE2 * Math.Sin(latitude) * Math.Sin(latitude)));
            latitude = Math.Atan2(z2 + (wgsE2 * wgsNu * Math.Sin(latitude)), p);
        }
        return new LatLon(Degrees(latitude), Degrees(Math.Atan2(y2, x2)));
    }

    // The Ordnance Survey's inverse Transverse Mercator: eastings and northings to latitude and
    // longitude on Airy 1830.
    private static (double Phi, double Lambda) OnAiry(double easting, double northing)
    {
        var e2 = 1 - (B * B / (A * A));
        var n = (A - B) / (A + B);
        var phi = Phi0;
        double m;
        do
        {
            phi += (northing - N0 - Meridional(phi)) / (A * F0);
            m = Meridional(phi);
        }
        while (Math.Abs(northing - N0 - m) >= 0.00001);

        var sin = Math.Sin(phi);
        var cos = Math.Cos(phi);
        var tan = Math.Tan(phi);
        var nu = A * F0 / Math.Sqrt(1 - (e2 * sin * sin));
        var rho = A * F0 * (1 - e2) / Math.Pow(1 - (e2 * sin * sin), 1.5);
        var eta2 = (nu / rho) - 1;

        var vii = tan / (2 * rho * nu);
        var viii = tan / (24 * rho * Math.Pow(nu, 3)) * (5 + (3 * tan * tan) + eta2 - (9 * tan * tan * eta2));
        var ix = tan / (720 * rho * Math.Pow(nu, 5)) * (61 + (90 * tan * tan) + (45 * Math.Pow(tan, 4)));
        var x = 1 / (cos * nu);
        var xi = 1 / (cos * 6 * Math.Pow(nu, 3)) * ((nu / rho) + (2 * tan * tan));
        var xii = 1 / (cos * 120 * Math.Pow(nu, 5)) * (5 + (28 * tan * tan) + (24 * Math.Pow(tan, 4)));
        var xiia = 1 / (cos * 5040 * Math.Pow(nu, 7)) * (61 + (662 * tan * tan) + (1320 * Math.Pow(tan, 4)) + (720 * Math.Pow(tan, 6)));

        var de = easting - E0;
        return (
            phi - (vii * de * de) + (viii * Math.Pow(de, 4)) - (ix * Math.Pow(de, 6)),
            Lambda0 + (x * de) - (xi * Math.Pow(de, 3)) + (xii * Math.Pow(de, 5)) - (xiia * Math.Pow(de, 7)));

        double Meridional(double at)
        {
            var d = at - Phi0;
            var s = at + Phi0;
            return B * F0 * (
                ((1 + n + (5.0 / 4 * n * n) + (5.0 / 4 * n * n * n)) * d)
                - (((3 * n) + (3 * n * n) + (21.0 / 8 * n * n * n)) * Math.Sin(d) * Math.Cos(s))
                + (((15.0 / 8 * n * n) + (15.0 / 8 * n * n * n)) * Math.Sin(2 * d) * Math.Cos(2 * s))
                - (35.0 / 24 * n * n * n * Math.Sin(3 * d) * Math.Cos(3 * s)));
        }
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Degrees(double radians) => radians * 180 / Math.PI;

    private static double Seconds(double seconds) => Radians(seconds / 3600);
}
