using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>The Admin panel's status (T129) and the jobs started from it (T130), as a spec sets them.</summary>
internal sealed class FakeAdmin : IAdmin
{
    public AdminStatus Status { get; set; } = AdminStatus.Unknown;

    /// <summary>Set, what the status is once a job is started.</summary>
    public AdminStatus? StatusAfterStart { get; set; }

    /// <summary>Set, why a job can't be started.</summary>
    public string? Refusal { get; set; }

    public List<string> Started { get; } = [];

    /// <summary>The backups asked to be restored (T132), by name.</summary>
    public List<string> Restoring { get; } = [];

    /// <summary>How many times "Back up now" was asked (T132), and what it says.</summary>
    public int BackedUp { get; private set; }

    public string BackUpSays { get; set; } = "Backed up.";

    /// <summary>Set, how many looks after a start before it shows (wend takes a while to begin).</summary>
    public int ShowsAfterLooks { get; set; }

    private AdminStatus? _pending;

    /// <summary>The job under way, as the spec sets it (T138).</summary>
    public UnderWay? UnderWay { get; set; }

    public Task<UnderWay?> UnderWayAsync(CancellationToken cancellationToken = default) => Task.FromResult(UnderWay);

    public Task<AdminStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        if (_pending is { } pending && ShowsAfterLooks-- <= 0) (Status, _pending) = (pending, null);
        return Task.FromResult(Status);
    }

    public Task<string?> StartAsync(string kind, string? backup = null, CancellationToken cancellationToken = default)
    {
        if (Refusal is not null) return Task.FromResult<string?>(Refusal);
        Started.Add(kind);
        if (backup is not null) Restoring.Add(backup);
        if (StatusAfterStart is { } after) _pending = after;
        if (ShowsAfterLooks == 0 && _pending is not null) (Status, _pending) = (_pending, null);
        return Task.FromResult<string?>(null);
    }

    public Task<string> BackUpAsync(CancellationToken cancellationToken = default)
    {
        BackedUp++;
        return Task.FromResult(BackUpSays);
    }
}
