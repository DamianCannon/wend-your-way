using Npgsql;

namespace WendYourWay.Data;

/// <summary>
/// Opens Wend Your Way's PostgreSQL database. Connection strings carry no password: it comes from
/// the libpq password file (%APPDATA%\postgresql\pgpass.conf) written by scripts/setup.ps1.
/// </summary>
public static class Database
{
    /// <summary>The app's database on this laptop.</summary>
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=cutthrough;Username=cutthrough";

    /// <summary>
    /// A data source that maps PostGIS geometry to NetTopologySuite types. Geometry is stored in
    /// British National Grid (EPSG:27700), so coordinates and lengths are in metres.
    /// </summary>
    public static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseNetTopologySuite();
        return builder.Build();
    }
}
