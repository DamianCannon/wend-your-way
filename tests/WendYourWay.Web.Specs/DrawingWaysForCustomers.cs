using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// Where a Loop runs on ways for customers only (T182, #186): the stretches of its line to draw striped,
/// each from the point before the first piece on such a way to the last such piece's end.
/// </summary>
public class DrawingWaysForCustomers
{
    //  Each point of the line is the end of a piece traced from one of the Loop's Traversals (on the map).
    private static readonly int[] Of = [0, 0, 1, 1, 1, 2, 3, 3, 4];

    [Test]
    public void A_Traversal_on_such_a_way_is_a_stretch_from_the_point_before_it() =>
        Assert.That(LineStretch.Where(Of, [1]), Is.EqualTo(new[] { new LineStretch(1, 4) }));

    [Test]
    public void Two_in_a_row_are_one_stretch() =>
        Assert.That(LineStretch.Where(Of, [1, 2]), Is.EqualTo(new[] { new LineStretch(1, 5) }));

    [Test]
    public void Two_apart_are_two() =>
        Assert.That(LineStretch.Where(Of, [1, 3]), Is.EqualTo(new[] { new LineStretch(1, 4), new LineStretch(5, 7) }));

    [Test]
    public void The_first_Traversal_starts_at_the_line_s_start() =>
        Assert.That(LineStretch.Where(Of, [0]), Is.EqualTo(new[] { new LineStretch(0, 1) }));

    [Test]
    public void None_is_none() =>
        Assert.That(LineStretch.Where(Of, []), Is.Empty);
}
