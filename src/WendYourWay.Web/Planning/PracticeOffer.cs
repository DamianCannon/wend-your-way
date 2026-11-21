using System.Diagnostics;
using WendYourWay.Data;

namespace WendYourWay.Web.Planning;

/// <summary>The practice map offered while there's no map of your own (T162, #166).</summary>
public interface IPracticeOffer
{
    /// <summary>Whether there's no map of your own yet: no Network built (or the first still building).</summary>
    Task<bool> NoMapOfYourOwnAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether it can be opened from here: through the launcher.</summary>
    bool CanOpen { get; }

    /// <summary>How to open it, said where it can't be opened from here, and in Help.</summary>
    string HowToOpen { get; }

    /// <summary>Opens it in a window of its own, through the launcher.</summary>
    Task OpenAsync();
}

/// <summary>
/// The practice map offered (T162, user chose "Offered when no map"): opened through the launcher with
/// --practice (T161) where it's built; in Docker, which has none, by its compose service.
/// </summary>
public sealed partial class PracticeOffer(string connectionString, string? launcher, bool inContainer, ILogger<PracticeOffer> logger) : IPracticeOffer
{
    /// <summary>The launcher built in this repository, if it is.</summary>
    public static string? Launcher(string contentRoot) =>
        WendLauncher.RepositoryRoot(contentRoot) is { } root
        && Path.Combine(root, "src", "WendYourWay.Launcher", "bin", "Release", "net10.0-windows", "WendYourWay.Launcher.exe") is var exe && File.Exists(exe) ? exe : null;

    /// <summary>Whether the app runs in a container (Docker's own images say so).</summary>
    public static bool InContainer => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    /// <summary>How it's opened: by the launcher's shortcut, Docker's service, or by hand.</summary>
    public static string HowTo(bool launcher, bool inContainer) =>
        launcher ? "the Wend Your Way – practice map shortcut in the Start menu"
        : inContainer ? "docker compose --profile practice up -d practice, then http://127.0.0.1:5020"
        : "dotnet run -c Release --project src/WendYourWay.Cli -- practice, then the planner with --Practice=true";

    public bool CanOpen => launcher is not null;

    public string HowToOpen => HowTo(launcher is not null, inContainer);

    public async Task<bool> NoMapOfYourOwnAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return !await PracticeMap.IsBuiltAsync(connectionString, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Not knowing is no reason to say there's no map.
            NotKnown(logger, e);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell whether there's a map of your own.")]
    private static partial void NotKnown(ILogger logger, Exception exception);

    public Task OpenAsync()
    {
        if (launcher is null) return Task.CompletedTask;
        var start = new ProcessStartInfo(launcher) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(launcher)! };
        // As the launcher knows it (WendYourWay.Launching.Practice.Flag).
        start.ArgumentList.Add("--practice");
        Process.Start(start)?.Dispose();
        return Task.CompletedTask;
    }
}
