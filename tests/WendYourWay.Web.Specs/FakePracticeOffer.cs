using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for the practice map's offer (T162): a map of your own or not, a launcher or not.</summary>
internal sealed class FakePracticeOffer : IPracticeOffer
{
    public bool NoMap { get; set; }

    public bool CanOpen { get; set; } = true;

    public string HowToOpen { get; set; } = "the Wend Your Way – practice map shortcut in the Start menu";

    /// <summary>How many times it was opened.</summary>
    public int Opened { get; private set; }

    public Task<bool> NoMapOfYourOwnAsync(CancellationToken cancellationToken = default) => Task.FromResult(NoMap);

    public Task OpenAsync()
    {
        Opened++;
        return Task.CompletedTask;
    }
}
