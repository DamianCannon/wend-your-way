using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using WendYourWay.Web;
using WendYourWay.Web.Components;
using WendYourWay.Web.Planning;
using WendYourWay.Data.UserData;

var builder = WebApplication.CreateBuilder(args);

// This app only ever runs on the user's own laptop. Secrets (the Thunderforest key) live in
// `dotnet user-secrets` whatever the environment.
builder.Configuration.AddUserSecrets<Program>(optional: true);
// The command line still wins, as it would without the line above: a run can start somewhere
// public (`--Home:Latitude=… --Home:Longitude=…`) rather than at home.
builder.Configuration.AddCommandLine(args);

// It also runs from source, so Blazor's own scripts are served from the build's static web
// assets in every environment, not only in Development.
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Settings are read from the app's final configuration when first needed, not while it's still
// being assembled, so every source (user-secrets, environment, a test's overrides) has its say.
builder.Services.AddSingleton(services => MapTiles.From(services.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(services => PlannerSettings.From(services.GetRequiredService<IConfiguration>()));

// The Network, in the app's own database (scripts/setup.ps1, `wend network build`).
// On the practice map (T161, Practice=true), the practice database beside it.
static string ConnectionString(IConfiguration configuration) =>
    PlannerSettings.IsPractice(configuration) ? PracticeMap.ConnectionStringOf(OwnDatabase(configuration)) : OwnDatabase(configuration);
static string OwnDatabase(IConfiguration configuration) => configuration.GetConnectionString("WendYourWay") ?? Database.DefaultConnectionString;
builder.Services.AddSingleton(services => Database.CreateDataSource(ConnectionString(services.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<INetworkSlices, PostgisNetworkSlices>();
builder.Services.AddSingleton<MapGeometry>();
builder.Services.AddSingleton(ScoringWeights.Default);
builder.Services.AddSingleton(services => new TerrainFolder(Terrain(services.GetRequiredService<IConfiguration>(), builder.Environment)));
builder.Services.AddSingleton<ILoopPlanner>(services => new LoopPlanner(
    services.GetRequiredService<INetworkSlices>(), services.GetRequiredService<MapGeometry>(), services.GetRequiredService<ScoringWeights>(),
    services.GetRequiredService<TerrainFolder>().Grid, new RouteMatching(services.GetRequiredService<Npgsql.NpgsqlDataSource>())));
// Saved Routes moved onto the map as it is (T136).
builder.Services.AddSingleton<ISavedRouteMoves>(services => new SavedRouteMoves(
    services.GetRequiredService<ISavedRoutes>(), services.GetRequiredService<ILoopPlanner>(), services.GetRequiredService<Npgsql.NpgsqlDataSource>()));
builder.Services.AddSingleton<IOutingViews, PostgisOutingViews>();
builder.Services.AddSingleton<ILastRequest, PostgisLastRequest>();
builder.Services.AddSingleton<OffRoadTiles>();
builder.Services.AddSingleton<ICarParks, PostgisCarParks>();
builder.Services.AddSingleton<IAmenities, PostgisAmenities>();
builder.Services.AddSingleton<IGravelHotspots, PostgisGravelHotspots>();
builder.Services.AddSingleton<ISavedRoutes, PostgisSavedRoutes>();
builder.Services.AddSingleton<IMarks, PostgisMarks>();
// Backups under way finished before the app stops (T138): closing its window stops it. The host
// waits for them up to its shutdown timeout, long enough for a backup.
builder.Services.AddSingleton<WendYourWay.Web.Planning.WorkUnderWay>();
builder.Services.AddHostedService(services => services.GetRequiredService<WendYourWay.Web.Planning.WorkUnderWay>());
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromMinutes(3));
// Wend Your Way at a glance, for the Admin panel (T129); backed up and restored from it (T132).
builder.Services.AddSingleton<WendYourWay.Web.Planning.IAdmin>(services => new WendYourWay.Web.Planning.AdminView(
    services.GetRequiredService<Npgsql.NpgsqlDataSource>(),
    ConnectionString(services.GetRequiredService<IConfiguration>()),
    UserDataBackups.FolderOf(UserDataBackups.Folder(services.GetRequiredService<IConfiguration>()["Backup:Folder"]), ConnectionString(services.GetRequiredService<IConfiguration>())),
    services.GetRequiredService<IConfiguration>()["MapUpdate:Data"] ?? Path.Combine(WendYourWay.Web.Planning.WendLauncher.RepositoryRoot(builder.Environment.ContentRootPath) ?? builder.Environment.ContentRootPath, "data"),
    services.GetRequiredService<TimeProvider>(),
    new WendYourWay.Web.Planning.WendLauncher(builder.Environment.ContentRootPath, ConnectionString(services.GetRequiredService<IConfiguration>()), services.GetRequiredService<TimeProvider>()),
    services.GetRequiredService<IConfiguration>()["Import:Folder"],
    services.GetRequiredService<WendYourWay.Web.Planning.WorkUnderWay>(),
    services.GetRequiredService<WendYourWay.Web.Planning.HeldBackAtStart>()));
builder.Services.AddSingleton<WendYourWay.Web.Planning.HeldBackAtStart>();
// The map brought up to date from the app (T128): `wend update` started in a process of its own.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WendYourWay.Web.Planning.IMapUpdater>(services => new WendYourWay.Web.Planning.ProcessMapUpdater(
    services.GetRequiredService<Npgsql.NpgsqlDataSource>(), ConnectionString(services.GetRequiredService<IConfiguration>()),
    builder.Environment.ContentRootPath, services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<ILogger<WendYourWay.Web.Planning.ProcessMapUpdater>>(),
    services.GetRequiredService<IConfiguration>()["MapUpdate:Source"], services.GetRequiredService<IConfiguration>()["MapUpdate:Data"]));
builder.Services.AddSingleton<IRouteTurns, RouteTurns>();
// The practice map offered while there's no map of your own (T162): through the launcher, if it's built here.
builder.Services.AddSingleton<IPracticeOffer>(services => new PracticeOffer(
    OwnDatabase(services.GetRequiredService<IConfiguration>()), PracticeOffer.InContainer,
    services.GetRequiredService<ILogger<PracticeOffer>>()));
// A route to the phone (T099): a listener of its own on the home network, when asked; the app
// itself stays on this laptop. Disposed, and so stopped, with the app.
builder.Services.AddSingleton<WendYourWay.Web.Phone.IPhoneShare>(_ => WendYourWay.Web.Phone.PhoneShare.OnTheHomeNetwork());
builder.Services.AddSingleton<ILoopPreviews, LoopPreviews>();
builder.Services.AddSingleton<WendYourWay.Data.Places.IPlaceSearch, WendYourWay.Data.Places.PostgisPlaceSearch>();

var app = builder.Build();

// Migrated as it starts, so a new table never stops it until a `wend` command runs (T056); but not
// while a map update, a build or another job runs, which the changes would be made under (#153, T149).
var started = await StartUp.PrepareAsync(ConnectionString(app.Configuration));
if (started.Migrated is { Count: > 0 } applied) Log.Migrated(app.Logger, applied);
if (started.JobRunning) Log.HeldBack(app.Logger, started.HeldBack);
app.Services.GetRequiredService<WendYourWay.Web.Planning.HeldBackAtStart>().Migrations = started.HeldBack;

// The practice map opened afresh (T161): what was done there before emptied, its map kept; never backed up.
var practice = PlannerSettings.IsPractice(app.Configuration);
if (practice) await PracticeMap.EmptyAsync(ConnectionString(app.Configuration));

// The user's own data backed up as the app starts, when the last backup is a day old (T102): in
// the background, so the map isn't kept waiting; Backup:Folder, else Wend Your Way Backups in OneDrive.
// Finished before the app stops, not cut off by it (T138). Not while a job runs (#153, T149): an update
// is moving Saved Routes and rescoring, and backs up as it ends; the next start after does it, if still due.
var backups = UserDataBackups.Folder(app.Configuration["Backup:Folder"]);
if (!practice && !started.JobRunning && UserDataBackups.FolderOf(backups, ConnectionString(app.Configuration)) is { } ours && UserDataBackups.IsDue(ours, DateTimeOffset.Now))
    _ = app.Services.GetRequiredService<WendYourWay.Web.Planning.WorkUnderWay>().Run(() =>
        UserDataBackups.AutomaticallyAsync(ConnectionString(app.Configuration), backups, line => Log.BackedUp(app.Logger, line), CancellationToken.None));

if (string.IsNullOrEmpty(app.Configuration["Thunderforest:ApiKey"])) Log.NoTileKey(app.Logger);
if (app.Services.GetRequiredService<TerrainFolder>().Grid is null) Log.NoTerrain(app.Logger);

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
// The Off-road networks as map tiles (T022b), read-only, for the part of the map in view.
app.MapGet("/tiles/offroad/{z:int}/{x:int}/{y:int}", async (int z, int x, int y, OffRoadTiles tiles, CancellationToken cancellationToken) =>
    z is < 0 or > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z
        ? Results.NotFound()
        : Results.Text(await tiles.JsonAsync(z, x, y, cancellationToken), "application/json"));

// The cafés and toilets in view (T067), for the overlay that shows them from zoom 13: a view no
// wider than zoom 11's, so a mistaken or crafted request can't read a whole country.
app.MapGet("/amenities", async (double south, double west, double north, double east, IAmenities amenities, CancellationToken cancellationToken) =>
    south >= north || west >= east || north - south > 0.5 || east - west > 1
        ? Results.BadRequest()
        : Results.Json((await amenities.InViewAsync(new MapBounds(south, west, north, east), cancellationToken))
            .Select(a => new { kind = a.Kind == AmenityKind.Cafe ? "cafe" : "toilets", a.Name, lat = a.At.Latitude, lon = a.At.Longitude })));

// The Gravel hotspots (T072), for their overlay: all of them, a couple of hundred, read once.
app.MapGet("/gravel-hotspots", async (IGravelHotspots hotspots, CancellationToken cancellationToken) =>
    Results.Json((await hotspots.AllAsync(cancellationToken: cancellationToken)).Select(h => new
    {
        near = h.Near, lat = h.At.Latitude, lon = h.At.Longitude, km = Math.Round(h.Km),
        south = h.Box.South, west = h.Box.West, north = h.Box.North, east = h.Box.East,
    })));

app.Run();

// OS Terrain 50's tiles for elevation profiles (T051): Terrain:Folder, or data/terrain50/asc in
// the repository the app runs from; none if they aren't there.
static TerrainGrid? Terrain(IConfiguration configuration, IHostEnvironment environment)
{
    if (configuration["Terrain:Folder"] is { Length: > 0 } folder) return new TerrainGrid(folder);
    for (var dir = new DirectoryInfo(environment.ContentRootPath); dir is not null; dir = dir.Parent)
    {
        var usual = Path.Combine(dir.FullName, "data", "terrain50", "asc");
        if (Directory.Exists(usual)) return new TerrainGrid(usual);
    }
    return null;
}

/// <summary>OS Terrain 50's tiles, if the app has them.</summary>
internal sealed record TerrainFolder(TerrainGrid? Grid);
