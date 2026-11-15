using System.Diagnostics.CodeAnalysis;
using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Loops;

/// <summary>
/// A generated route that starts and finishes at the same place: its start junction, the Anchors
/// it was generated through (in order, for numbering on the map), and its Traversals in order,
/// all over the <see cref="Network.NetworkSlice"/> it was generated on.
/// </summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Loop is the domain's central term (CONTEXT.md); nothing consumes this assembly from Visual Basic.")]
public sealed record Loop(int Start, IReadOnlyList<int> Anchors, IReadOnlyList<Traversal> Traversals);
