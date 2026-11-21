using WendYourWay.Data;

namespace WendYourWay.Web.Planning;

/// <summary>The practice map offered while there's no map of your own (T162, #166).</summary>
public interface IPracticeOffer
{
    /// <summary>Whether there's no map of your own yet: no Network built (or the first still building).</summary>
    Task<bool> NoMapOfYourOwnAsync(CancellationToken cancellationToken = default);

    /// <summary>How to open it, said in a browser, where it can't be switched to from here (T163).</summary>
    string HowToOpen { get; }
}

/// <summary>
/// The practice map offered (T162, user chose "Offered when no map"): switched to in the launcher's
/// window (T163); in a browser, by Docker's compose service or by hand.
/// </summary>
public sealed partial class PracticeOffer(string connectionString, bool inContainer, ILogger<PracticeOffer> logger) : IPracticeOffer
{
    /// <summary>Whether the app runs in a container (Docker's own images say so).</summary>
    public static bool InContainer => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    /// <summary>How it's opened in a browser: by Docker's service, or by hand.</summary>
    public static string HowTo(bool inContainer) =>
        inContainer ? "docker compose --profile practice up -d practice, then http://127.0.0.1:5020"
        : "open Wend Your Way from its icon and switch to it here, or run wend practice and the planner with --Practice=true";

    public string HowToOpen => HowTo(inContainer);

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
}
