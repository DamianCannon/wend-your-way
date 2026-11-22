using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// T021: where a Trail Ride may go (SPEC R9.6; PLAN T021). Which months OSM closes a way to a
/// trail bike, read from its bans, its conditional tags and its traffic_regulation_order; and a
/// Trail Ride's access, from the tags, whether the Segment is on a Definitive Map byway and
/// whether its authority's Definitive Map is open. Pure SQL functions, one query a case.
/// </summary>
public static class ReadingClosures
{
    private static NpgsqlDataSource? _dataSource;

    private static NpgsqlDataSource DataSource
    {
        get
        {
            if (_dataSource is null)
            {
                TestDatabase.Wipe();
                Migrations.Apply(TestDatabase.ConnectionString);
                _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
            }
            return _dataSource;
        }
    }

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    // "Jan Feb Oct", "all" or "none" as the bits motor_closed_months() gives: January the lowest.
    private static int Mask(string months) => months switch
    {
        "all" => 4095,
        "none" => 0,
        _ => months.Split(' ').Aggregate(0, (mask, month) => mask | 1 << Array.IndexOf(Months, month)),
    };

    private static async Task<T> Ask<T>(string sql, string tags)
    {
        await using var command = DataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("tags", tags));
        return (T)(await command.ExecuteScalarAsync())!;
    }

    [TestFixture]
    [NonParallelizable]
    public class Closed_months
    {
        [TestCase("""{"highway":"track"}""", "none", Description = "nothing said: open")]
        [TestCase("""{"highway":"track","motor_vehicle":"no"}""", "all")]
        [TestCase("""{"highway":"track","motor_vehicle":"private"}""", "all")]
        [TestCase("""{"highway":"track","motor_vehicle":"agricultural"}""", "all")]
        [TestCase("""{"highway":"track","motor_vehicle":"forestry"}""", "all")]
        [TestCase("""{"highway":"track","motor_vehicle":"permit"}""", "all")]
        [TestCase("""{"highway":"track","motor_vehicle":"destination"}""", "all")]
        [TestCase("""{"highway":"track","motor_vehicle":"delivery"}""", "all")]
        [TestCase("""{"highway":"track","motorcycle":"no"}""", "all")]
        [TestCase("""{"highway":"track","vehicle":"no"}""", "all")]
        [TestCase("""{"highway":"track","access":"no"}""", "all")]
        [TestCase("""{"highway":"track","motor_vehicle":"discouraged"}""", "none", Description = "discouraged isn't banned")]
        [TestCase("""{"highway":"track","access":"no","motorcycle":"yes"}""", "none", Description = "the trail bike's own tag decides")]
        [TestCase("""{"highway":"track","motor_vehicle":"no","motorcycle":"designated"}""", "none")]
        [TestCase("""{"highway":"track","motor_vehicle":"yes","access":"no"}""", "none", Description = "the more particular tag decides")]
        public async Task Then_a_ban_closes_every_month(string tags, string months) =>
            Assert.That(await Ask<int>("select motor_closed_months(@tags::jsonb)", tags), Is.EqualTo(Mask(months)));

        [TestCase("no @ (Oct 01-Apr 30)", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("no @ (Oct-Apr)", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("no @ Oct-Apr", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("no @ (1 Oct - 30 Apr)", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("no @ (Oct 1 - Apr 30)", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("no @ (1 November-31 March)", "Jan Feb Mar Nov Dec")]
        [TestCase("no @ (October - April)", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("private @ Oct-Apr", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("no @ (Apr - Oct)", "Apr May Jun Jul Aug Sep Oct", Description = "a summer closure")]
        [TestCase("no @ (Oct 31-Apr 30)", "Jan Feb Mar Apr Oct Nov Dec", Description = "a part month counts whole")]
        public async Task Then_a_seasonal_closure_closes_its_months(string condition, string months) =>
            Assert.That(await Ask<int>("select motor_closed_months(@tags::jsonb)",
                $$"""{"highway":"track","motor_vehicle:conditional":"{{condition}}"}"""), Is.EqualTo(Mask(months)));

        [TestCase("""{"highway":"track","motor_vehicle":"no","motor_vehicle:conditional":"yes @ (Apr-Oct)"}""", "Jan Feb Mar Nov Dec")]
        [TestCase("""{"highway":"track","motor_vehicle":"no","motor_vehicle:conditional":"designated @ (May-Nov)"}""", "Jan Feb Mar Apr Dec")]
        [TestCase("""{"highway":"track","motor_vehicle":"no","motor_vehicle:conditional":"permissive @ (Apr - Oct)"}""", "Jan Feb Mar Nov Dec")]
        [TestCase("""{"highway":"track","motor_vehicle":"no","motor_vehicle:conditional":"yes @ permit_holder"}""", "all", Description = "a permit isn't ours")]
        [TestCase("""{"highway":"track","motor_vehicle":"no","motor_vehicle:conditional":"yes @ seasonal"}""", "all", Description = "a season not said")]
        public async Task Then_a_seasonal_opening_opens_its_months(string tags, string months) =>
            Assert.That(await Ask<int>("select motor_closed_months(@tags::jsonb)", tags), Is.EqualTo(Mask(months)));

        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (wheels>2)"}""", "none", Description = "more than two wheels: not a trail bike")]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (Oct-Apr AND wheels > 2)"}""", "none")]
        [TestCase("""{"highway":"track","access:conditional":"no @ (Oct-Apr)"}""", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("""{"highway":"track","vehicle:conditional":"no @ (Oct-Apr)"}""", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("""{"highway":"track","motorcycle:conditional":"no @ (Oct-Apr)"}""", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (Oct-Apr)","motorcycle:conditional":"yes @ (Oct-Apr)"}""", "none", Description = "the trail bike's own rule last")]
        [TestCase("""{"highway":"track","access:conditional":"no @1 oct-30 apr; no @ if gates shut; ; except private permit; except footpath"}""", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"private @ (Oct 01-Apr 30||\"when barrier is locked closed\")"}""", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (Oct-Apr; when wet)"}""", "Jan Feb Mar Apr Oct Nov Dec", Description = "a semicolon in brackets is an or")]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (Oct-Apr AND wet)"}""", "Jan Feb Mar Apr Oct Nov Dec", Description = "closed then, at least")]
        public async Task Then_each_rule_is_read_for_a_trail_bike(string tags, string months) =>
            Assert.That(await Ask<int>("select motor_closed_months(@tags::jsonb)", tags), Is.EqualTo(Mask(months)));

        [TestCase("no @ motor_vehicle", "all")]
        [TestCase("no @ vehicle; yes @ bicycle", "all")]
        [TestCase("no @ motor_vehicle; yes @ motorcycle", "none", Description = "trail bikes let through")]
        [TestCase("no @ motor_vehicle;yes @ (motorcycle)", "none")]
        [TestCase("no @ (motor_vehicle AND wheels>2)", "none")]
        [TestCase("no @ (motor_vehicle AND Oct 31-Apr 30)", "Jan Feb Mar Apr Oct Nov Dec")]
        [TestCase("no @ motor_vehicle; yes @ permit_holder", "all")]
        [TestCase("no @ motor_vehicle; no @ horse-drawn_vehicle", "all")]
        [TestCase("no @ horse", "none", Description = "not about motors")]
        [TestCase("yes @ motorcycle; no @ (Oct-Apr)", "none", Description = "a rule for everyone is read once, before the trail bike's")]
        public async Task Then_a_traffic_regulation_order_is_read(string order, string months) =>
            Assert.That(await Ask<int>("select motor_closed_months(@tags::jsonb)",
                $$"""{"highway":"track","traffic_regulation_order":"{{order}}"}"""), Is.EqualTo(Mask(months)));

        [TestCase("no @ (Mo-Fr 08:15-09:15,14:45-15:45)")]
        [TestCase("no @ snow")]
        [TestCase("no @ (learner)")]
        [TestCase("permit @ (20 Mar-31 Oct 10:00-18:00)")]
        public async Task Then_a_closure_it_cannot_read_closes_no_month(string condition) =>
            Assert.That(await Ask<int>("select motor_closed_months(@tags::jsonb)",
                $$"""{"highway":"track","motor_vehicle:conditional":"{{condition}}"}"""), Is.Zero);
    }

    [TestFixture]
    [NonParallelizable]
    public class A_closure_it_cannot_read
    {
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (Mo-Fr 08:15-09:15,14:45-15:45)"}""", true, Description = "hours, off the roads")]
        [TestCase("""{"highway":"track","access:conditional":"no @ snow"}""", true)]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"permit @ (20 Mar-31 Oct 10:00-18:00)"}""", true)]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"private @ (Oct 01-Apr 30||\"when barrier is locked closed\")"}""", true)]
        [TestCase("""{"highway":"track","traffic_regulation_order":"no @ (motor_vehicle AND Mo-Fr 07:00-19:00)"}""", true)]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (Oct-Apr; when wet)"}""", true)]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (Oct-Apr)"}""", false, Description = "months, read")]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"yes @ (Mo-Fr 08:00-18:00)"}""", false, Description = "an opening is never trusted unread")]
        [TestCase("""{"highway":"track","motor_vehicle:conditional":"no @ (wheels>2)"}""", false)]
        [TestCase("""{"highway":"track","traffic_regulation_order":"no @ (horse AND Mo-Fr 07:00-19:00)"}""", false, Description = "not about motors")]
        [TestCase("""{"highway":"residential","motor_vehicle:conditional":"no @ (Mo-Fr 08:15-09:15,14:45-15:45)"}""", false, Description = "a school street: on a road, passed over")]
        [TestCase("""{"highway":"track"}""", false)]
        public async Task Then_off_the_roads_it_says_check_this(string tags, bool unread) =>
            Assert.That(await Ask<bool>("select motor_closure_unread(@tags::jsonb)", tags), Is.EqualTo(unread));
    }

    [TestFixture]
    [NonParallelizable]
    public class Trail_Ride_access
    {
        private static Task<string> Access(string tags, bool onByway, bool covered = true) =>
            Ask<string>($"select trail_access(@tags::jsonb, {onByway}, {covered})::text", tags);

        [TestCase("""{"highway":"track"}""")]
        [TestCase("""{"highway":"track","designation":"byway_open_to_all_traffic"}""")]
        [TestCase("""{"highway":"byway"}""")]
        [TestCase("""{"highway":"unclassified"}""", Description = "a road on a byway's line")]
        [TestCase("""{"highway":"path"}""")]
        public async Task Then_a_Definitive_Map_byway_may_be_ridden(string tags) =>
            Assert.That(await Access(tags, onByway: true), Is.EqualTo("allowed"));

        [TestCase("""{"highway":"track","designation":"public_footpath"}""", Description = "OSM's word wins where it says less")]
        [TestCase("""{"highway":"track","designation":"public_bridleway"}""")]
        [TestCase("""{"highway":"track","designation":"restricted_byway"}""")]
        [TestCase("""{"highway":"footway"}""", Description = "a pavement beside a byway")]
        [TestCase("""{"highway":"cycleway"}""")]
        [TestCase("""{"highway":"steps"}""")]
        [TestCase("""{"highway":"pedestrian"}""")]
        public async Task Then_a_lesser_way_beside_a_byway_may_not(string tags) =>
            Assert.That(await Access(tags, onByway: true), Is.EqualTo("forbidden"));

        //  T182 (#186): a way for customers only is ridden as if it weren't, striped and costed a little more.
        [TestCase("""{"highway":"service","access":"customers"}""", "allowed")]
        [TestCase("""{"highway":"unclassified","motor_vehicle":"customers"}""", "allowed")]
        public async Task Then_a_way_for_customers_only_may_be_ridden(string tags, string access) =>
            Assert.That(await Access(tags, onByway: false), Is.EqualTo(access));

        [Test]
        public async Task Then_a_byway_through_land_for_customers_may_still_be_ridden() =>
            Assert.That(await Access("""{"highway":"track","designation":"byway_open_to_all_traffic","access":"customers"}""", onByway: true), Is.EqualTo("allowed"));

        [TestCase("""{"highway":"trunk"}""", Description = "AC4: an A-road joins byways")]
        [TestCase("""{"highway":"primary"}""")]
        [TestCase("""{"highway":"secondary"}""")]
        [TestCase("""{"highway":"tertiary"}""")]
        [TestCase("""{"highway":"unclassified"}""")]
        [TestCase("""{"highway":"residential"}""")]
        [TestCase("""{"highway":"living_street"}""")]
        [TestCase("""{"highway":"service"}""")]
        [TestCase("""{"highway":"primary_link"}""")]
        [TestCase("""{"highway":"unclassified","designation":"unclassified_highway"}""", Description = "a tarred lane is a road")]
        public async Task Then_a_road_may_be_ridden(string tags) =>
            Assert.That(await Access(tags, onByway: false), Is.EqualTo("allowed"));

        [TestCase("""{"highway":"motorway"}""", Description = "a trail bike on a motorway is no Trail Ride")]
        [TestCase("""{"highway":"motorway_link"}""")]
        [TestCase("""{"highway":"service","service":"driveway"}""")]
        [TestCase("""{"highway":"track"}""", Description = "no recorded right")]
        [TestCase("""{"highway":"path"}""")]
        [TestCase("""{"highway":"bridleway"}""")]
        [TestCase("""{"highway":"footway"}""")]
        [TestCase("""{"highway":"cycleway"}""")]
        [TestCase("""{"highway":"track","designation":"byway_open_to_all_traffic"}""", Description = "OSM's byway on no Definitive Map line")]
        [TestCase("""{"highway":"track","designation":"public_byway"}""")]
        public async Task Then_anything_else_may_not(string tags) =>
            Assert.That(await Access(tags, onByway: false), Is.EqualTo("forbidden"));

        [TestCase("""{"highway":"service","service":"driveway","motorcycle":"yes"}""")]
        [TestCase("""{"highway":"service","service":"driveway","access":"yes"}""")]
        public async Task Then_a_driveway_open_to_all_may_be_ridden(string tags) =>
            Assert.That(await Access(tags, onByway: false), Is.EqualTo("allowed"));

        [TestCase("""{"highway":"track","designation":"unclassified_highway"}""", Description = "a green lane")]
        [TestCase("""{"highway":"track","designation":"unclassified_highway;byway_open_to_all_traffic"}""")]
        public async Task Then_a_green_lane_says_check_this(string tags) =>
            Assert.That(await Access(tags, onByway: false), Is.EqualTo("unconfirmed"));

        [Test]
        public async Task Then_OSMs_byway_where_no_Definitive_Map_is_open_says_check_this() =>
            Assert.That(await Access("""{"highway":"track","designation":"byway_open_to_all_traffic"}""", onByway: false, covered: false),
                Is.EqualTo("unconfirmed"));

        [Test]
        public async Task Then_a_byway_with_a_closure_it_cannot_read_says_check_this() =>
            Assert.That(await Access("""{"highway":"track","access:conditional":"no @ snow"}""", onByway: true), Is.EqualTo("unconfirmed"));

        [Test]
        public async Task Then_a_road_with_hours_it_cannot_read_may_be_ridden() =>
            Assert.That(await Access("""{"highway":"residential","motor_vehicle:conditional":"no @ (Mo-Fr 08:15-09:15)"}""", onByway: false),
                Is.EqualTo("allowed"));
    }
}
