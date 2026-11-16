using WendYourWay.Launching;

// Wend Your Way opened with one click (#142, T138, STORY-049; user: "I want a one click method to start
// it up and open the page"). `WendYourWay.Launcher.exe` opens it; `--install` makes its shortcuts;
// anything after `--` goes to the app (another address or database, for trying it by hand).
namespace WendYourWay.Launcher;

internal static class Program
{
    // Windows Forms and WebView2 need the main thread single-threaded for COM.
    [STAThread]
    private static int Main(string[] args)
    {
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(NativeMethods.AppId);
        var launcherArguments = args.TakeWhile(a => a != "--").ToList();
        var appArguments = args.SkipWhile(a => a != "--").Skip(1).ToList();
        var root = Repository.Root(AppContext.BaseDirectory);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wend Your Way");
        Directory.CreateDirectory(folder);
        var logs = Path.Combine(root ?? folder, "data", "logs");
        Directory.CreateDirectory(logs);
        var launcherLog = Path.Combine(logs, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");
        void Log(string line) => File.AppendAllText(launcherLog, $"{DateTime.Now:HH:mm:ss} {line.TrimEnd()}{Environment.NewLine}");

        if (launcherArguments.Contains("--install"))
        {
            foreach (var made in Shortcuts.Make(Environment.ProcessPath!)) Log($"Shortcut made: {made}");
            return 0;
        }

        // Once (AC5): clicked again while open, the window already open is brought forward.
        using var once = new Mutex(true, @"Local\WendYourWay.Launcher", out var first);
        using var bringForward = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WendYourWay.Launcher.Forward");
        if (!first)
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.AnyProcess);
            bringForward.Set();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using var icon = new Icon(typeof(StartingWindow).Assembly.GetManifestResourceStream("WendYourWay.Launcher.wend-your-way.ico") ?? File.OpenRead(Path.Combine(AppContext.BaseDirectory, "wend-your-way.ico")));
        using var context = new Opening(root, appArguments, folder, logs, icon, bringForward, Log);
        Application.Run(context);
        return 0;
    }
}

/// <summary>The repository the launcher was built in: WendYourWay.sln's folder, from the launcher's own upwards.</summary>
internal static class Repository
{
    public static string? Root(string from)
    {
        for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WendYourWay.sln"))) return dir.FullName;
        return null;
    }
}

/// <summary>
/// From the click to the planner, and from closing it to the app stopped: the starting window
/// says each step (AC7); what's found decides what's done (AC5, AC6); a rebuild if the code
/// changed (AC4); the app stopped when its window closes, if the launcher started it (AC3).
/// </summary>
internal sealed class Opening : ApplicationContext
{
    private readonly StartingWindow _starting;
    private readonly TheApp? _app;
    private readonly string? _root;
    private readonly string _folder;
    private readonly string _logs;
    private readonly Icon _icon;
    private readonly Action<string> _log;
    private readonly RegisteredWaitHandle _forward;
    private PlannerWindow? _planner;
    private bool _started;
    private bool _closingDown;

    public Opening(string? root, IReadOnlyList<string> appArguments, string folder, string logs, Icon icon, EventWaitHandle bringForward, Action<string> log)
    {
        (_root, _folder, _logs, _icon, _log) = (root, folder, logs, icon, log);
        _starting = new StartingWindow(icon);
        // Closed while the app it started is still starting (#151, T147): the app stopped too, not left
        // running with no window, where every later click would open onto it and never stop it.
        _starting.FormClosing += async (_, e) =>
        {
            if (_planner is not null || _closingDown || !_started || _app is null) return;
            e.Cancel = true;
            _closingDown = true;
            _starting.Step("Stopping Wend Your Way…");
            await _app.StopAsync();
            ExitThread();
        };
        _starting.FormClosed += (_, _) => { if (_planner is null) ExitThread(); };
        _app = root is null ? null : new TheApp(root, appArguments, log);
        // Brought back up if minimised (#151, T147): Activate alone leaves it minimised.
        _forward = ThreadPool.RegisterWaitForSingleObject(bringForward, (_, _) => _starting.BeginInvoke(() =>
        {
            if (_planner is { Visible: true }) _planner.BringForward();
            else _starting.Activate();
        }), null, -1, false);
        _starting.Shown += async (_, _) => await OpenAsync();
        _starting.Show();
    }

    private async Task OpenAsync()
    {
        if (_app is null)
        {
            _starting.Stopped("Wend Your Way's folder can't be found from where this launcher is. Build it there again (dotnet build -c Release src/WendYourWay.Launcher).");
            return;
        }
        _starting.Step("Checking…");
        var launch = Launch.Decide(ourWindowOpen: false, await _app.AnsweringAsync(), await TheApp.PostgreSqlUpAsync());
        _log($"Opening: {launch.Name}");
        if (launch == Launch.PostgreSqlDown || launch == Launch.PortTaken)
        {
            _starting.Stopped(Launch.Said(launch));
            return;
        }
        if (launch == Launch.StartTheApp)
        {
            if (_app.NeedsRebuilding())
            {
                _starting.Step("Updating Wend Your Way to the latest code: rebuilding (about a minute)…");
                if (await _app.RebuildAsync() is { } failed)
                {
                    _log($"Rebuild failed: {failed}");
                    _starting.Step($"The latest code didn't build ({failed.Split(Environment.NewLine)[0]}), so the last build that worked is opening.");
                    await Task.Delay(TimeSpan.FromSeconds(4));
                }
            }
            _starting.Step("Starting Wend Your Way…");
            if (!_app.Start(Path.Combine(_logs, $"app-{DateTime.Now:yyyy-MM-dd-HHmm}.log")))
            {
                _starting.Stopped("Wend Your Way isn't built yet: run dotnet build -c Release src/WendYourWay.Web once, then try again.");
                return;
            }
            _started = true;
            var ready = await _app.ReadyAsync();
            if (_closingDown) return;
            if (!ready)
            {
                _starting.Stopped($"Wend Your Way didn't start. Its log in {_logs} says why.");
                return;
            }
        }
        _starting.Step("Opening the planner…");
        _planner = new PlannerWindow(_app.Address, _icon, _folder);
        _planner.FormClosed += async (_, _) => await ClosedAsync();
        _planner.Show();
        _starting.Hide();
    }

    // The window closed (AC3): the app stopped if the launcher started it; one started some
    // other way left running.
    private async Task ClosedAsync()
    {
        if (_started && _app is not null)
        {
            _starting.Step("Stopping Wend Your Way… (a backup under way is finished first)");
            _starting.Show();
            await _app.StopAsync();
            _log("Stopped.");
        }
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _forward.Unregister(null);
            _starting.Dispose();
            _planner?.Dispose();
        }
        base.Dispose(disposing);
    }
}