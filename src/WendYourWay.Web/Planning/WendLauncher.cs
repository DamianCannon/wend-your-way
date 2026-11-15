using System.Diagnostics;
using WendYourWay.Data.Building;

namespace WendYourWay.Web.Planning;

/// <summary>
/// Starts a `wend` job from the app (T128, T130): the built wend, never `dotnet run` (a half-edited file
/// would fail it), and only if it was built from the app's own code; a process of its own with no
/// pipes to the app, so stopping the app doesn't stop the job. Its words go to `data/logs/`.
/// </summary>
public sealed class WendLauncher(string contentRoot, string connectionString, TimeProvider time)
{
    /// <summary>
    /// Starts `wend` with these arguments, its log named for the job; the reason why not if it can't be,
    /// or if it stops at once (a bad option, a database it can't reach: its log says which).
    /// </summary>
    public string? Start(string job, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var command = "wend " + string.Join(' ', arguments.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));
        if (RepositoryRoot(contentRoot) is not { } root) return $"Couldn't find Wend Your Way's folder to run {command} from.";
        var wend = Path.Combine(root, "src", "WendYourWay.Cli", "bin", "Release", "net10.0", "wend.dll");
        if (!File.Exists(wend)) return "wend isn't built: run dotnet build -c Release src/WendYourWay.Cli once, then try again.";
        if (!SameCode(Path.Combine(Path.GetDirectoryName(wend)!, "WendYourWay.Data.dll"), typeof(Jobs).Assembly.Location))
            return "wend was built from other code than the app: run dotnet build -c Release src/WendYourWay.Cli, then try again.";

        var log = Path.Combine(root, "data", "logs", $"{job}-{time.GetLocalNow():yyyy-MM-dd-HHmm}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { wend }.Concat(arguments).Concat(["--log", log, "--database", connectionString]))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process is null) return $"{command} couldn't be started.";
        if (process.WaitForExit(TimeSpan.FromSeconds(3)) && process.ExitCode != 0)
            return $"{command} stopped at once: see {log}.";
        return null;
    }

    /// <summary>The folder holding WendYourWay.sln, from the app's own, upwards; none if there isn't one.</summary>
    public static string? RepositoryRoot(string from)
    {
        for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WendYourWay.sln"))) return dir.FullName;
        return null;
    }

    /// <summary>Whether two builds of a library are of the same code: builds are deterministic, so the same bytes.</summary>
    public static bool SameCode(string a, string b) =>
        File.Exists(a) && File.Exists(b) && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
}
