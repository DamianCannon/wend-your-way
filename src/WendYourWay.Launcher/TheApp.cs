using System.Diagnostics;
using System.Net.Sockets;
using WendYourWay.Launching;

namespace WendYourWay.Launcher;

/// <summary>
/// The app as the launcher handles it (#142, T138): found already running, rebuilt if its code
/// changed, started as its built exe (its words to data/logs), and stopped gently, as Ctrl+C does,
/// so a backup under way finishes.
/// </summary>
internal sealed class TheApp(string root, IReadOnlyList<string> appArguments, Action<string> log)
{
    // An app idle a while can take some seconds over its first page (found by hand).
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>How long the app is given to stop: its host waits up to 3 minutes for a backup.</summary>
    private static readonly TimeSpan StopWithin = TimeSpan.FromMinutes(3.5);

    private Process? _process;

    public Uri Address { get; } = Launch.Address(appArguments);

    private string Bin(string project, string framework = "net10.0") => Path.Combine(root, "src", project, "bin", "Release", framework);

    /// <summary>What answers on the app's address: nothing listening is known at once; something listening is given time to answer.</summary>
    public async Task<App> AnsweringAsync()
    {
        if (!await ListeningAsync(Address.Host, Address.Port)) return App.None;
        try
        {
            return App.Of(await Http.GetStringAsync(Address));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return App.None;
        }
    }

    /// <summary>Whether PostgreSQL answers on this laptop.</summary>
    public static Task<bool> PostgreSqlUpAsync() => ListeningAsync("127.0.0.1", 5432);

    private static async Task<bool> ListeningAsync(string host, int port)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(2));
            return true;
        }
        catch (Exception e) when (e is SocketException or TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Whether its code changed since it was built (the app and wend both).</summary>
    public bool NeedsRebuilding()
    {
        var built = new[] { Path.Combine(Bin("WendYourWay.Web"), "WendYourWay.Web.dll"), Path.Combine(Bin("WendYourWay.Cli"), "wend.dll") }
            .Select(f => File.Exists(f) ? File.GetLastWriteTimeUtc(f) : (DateTime?)null).ToList();
        var stamp = Path.Combine(Bin("WendYourWay.Web"), ".launcher-built");
        DateTime? builtAt = built.Any(b => b is null) ? null : built.Min();
        if (builtAt is { } at && File.Exists(stamp) && File.GetLastWriteTimeUtc(stamp) > at) builtAt = File.GetLastWriteTimeUtc(stamp);
        return Rebuild.Needed(Rebuild.Sources(root), builtAt);
    }

    /// <summary>wend, then the app, built Release, so they're the same code; why not, if it failed.</summary>
    public async Task<string?> RebuildAsync()
    {
        foreach (var project in new[] { "WendYourWay.Cli", "WendYourWay.Web" })
        {
            var (code, output) = await RunAsync("dotnet", ["build", "-c", "Release", "--nologo", "-v", "q", Path.Combine("src", project)]);
            log(output);
            if (code != 0)
                return string.Join(Environment.NewLine, output.Split('\n').Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Select(l => l.Trim()).Distinct().Take(3));
        }
        await File.WriteAllTextAsync(Path.Combine(Bin("WendYourWay.Web"), ".launcher-built"), DateTime.UtcNow.ToString("O"));
        return null;
    }

    /// <summary>Starts the built app; false if there's no build to start.</summary>
    public bool Start(string logFile)
    {
        var exe = Path.Combine(Bin("WendYourWay.Web"), "WendYourWay.Web.exe");
        if (!File.Exists(exe)) return false;
        var start = new ProcessStartInfo(exe)
        {
            // Its content root, as `dotnet run` gives it: appsettings.json and wwwroot are found there.
            WorkingDirectory = Path.Combine(root, "src", "WendYourWay.Web"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in appArguments) start.ArgumentList.Add(argument);
        _process = Process.Start(start)!;
        var writer = new StreamWriter(logFile, append: true) { AutoFlush = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (writer) writer.WriteLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (writer) writer.WriteLine(e.Data); };
        _process.Exited += (_, _) => { lock (writer) writer.Dispose(); };
        _process.EnableRaisingEvents = true;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        return true;
    }

    /// <summary>Waits till the app answers; false if it stopped or didn't within a minute and a half.</summary>
    public async Task<bool> ReadyAsync()
    {
        for (var tries = 0; tries < 180; tries++)
        {
            if (_process is { HasExited: true }) return false;
            // Its own, still running: not another one that happens to answer (found by hand).
            if (await AnsweringAsync() == App.Ours) return _process is { HasExited: false };
            await Task.Delay(500);
        }
        return false;
    }

    /// <summary>Stops the app it started, as Ctrl+C does, waiting for it; killed only if it won't.</summary>
    public async Task StopAsync()
    {
        if (_process is not { HasExited: false } process) return;
        NativeMethods.FreeConsole();
        if (NativeMethods.AttachConsole((uint)process.Id))
        {
            NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, true);
            NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CtrlC, 0);
            NativeMethods.FreeConsole();
        }
        try
        {
            await process.WaitForExitAsync().WaitAsync(StopWithin);
        }
        catch (TimeoutException)
        {
            log("The app didn't stop when asked: stopped outright.");
            process.Kill();
        }
        finally
        {
            NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, false);
        }
    }

    private async Task<(int Code, string Output)> RunAsync(string file, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output + await errors);
    }
}
