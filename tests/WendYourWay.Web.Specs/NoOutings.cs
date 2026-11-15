using WendYourWay.Data.Outings;

namespace WendYourWay.Web.Specs;

/// <summary>No Outings imported (moved out of UsingThePlannerScreen, T118): the planner screen's default.</summary>
internal sealed class NoOutings : IOutingViews
{
    public Task<IReadOnlyList<OutingSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OutingSummary>>([]);

    public Task<OutingView?> ViewAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<OutingView?>(null);

    public Task RateAsync(int id, int? stars, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RenameAsync(int id, string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
