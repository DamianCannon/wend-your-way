using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for the practice map's offer (T162): a map of your own or not.</summary>
internal sealed class FakePracticeOffer : IPracticeOffer
{
    public bool NoMap { get; set; }

    public string HowToOpen { get; set; } = "docker compose --profile practice up -d practice, then http://127.0.0.1:5020";

    public Task<bool> NoMapOfYourOwnAsync(CancellationToken cancellationToken = default) => Task.FromResult(NoMap);
}
