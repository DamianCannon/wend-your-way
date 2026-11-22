using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// How an OSM way's tags become a ground class and Run access (CONTEXT.md: Off-road,
/// Traffic-free, Connector; Access). Pure SQL functions, so each case is one query.
/// </summary>
public static class ClassifyingWays
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

    private static async Task<string> Ask(string function, string tags)
    {
        await using var command = DataSource.CreateCommand($"select {function}(@tags::jsonb)::text");
        command.Parameters.Add(new NpgsqlParameter("tags", tags));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [TestFixture]
    [NonParallelizable]
    public class Ground
    {
        [TestCase("""{"highway":"residential"}""", "connector", Description = "a road carries traffic")]
        [TestCase("""{"highway":"primary"}""", "connector")]
        [TestCase("""{"highway":"service"}""", "connector")]
        [TestCase("""{"highway":"footway","footway":"sidewalk"}""", "connector", Description = "a pavement runs beside traffic (glossary: Connector)")]
        [TestCase("""{"highway":"path","path":"sidewalk"}""", "connector")]
        [TestCase("""{"highway":"service","service":"alley"}""", "traffic_free", Description = "glossary: alleys are Traffic-free")]
        [TestCase("""{"highway":"service","motor_vehicle":"no"}""", "traffic_free", Description = "a road closed to motor traffic, such as a park drive")]
        [TestCase("""{"highway":"residential","vehicle":"no"}""", "traffic_free")]
        [TestCase("""{"highway":"unclassified","access":"no","foot":"yes"}""", "traffic_free")]
        [TestCase("""{"highway":"service","access":"no","bus":"yes"}""", "connector", Description = "buses still run")]
        [TestCase("""{"highway":"service","motor_vehicle":"no","psv":"designated"}""", "connector")]
        [TestCase("""{"highway":"service","access":"no","motor_vehicle":"destination"}""", "connector", Description = "cars may still come and go")]
        [TestCase("""{"highway":"service","access":"private"}""", "connector", Description = "private roads still carry residents' cars")]
        [TestCase("""{"highway":"track","motor_vehicle":"no"}""", "off_road", Description = "only roads lose their traffic; a track keeps its ground")]
        [TestCase("""{"highway":"footway"}""", "traffic_free", Description = "an untagged footway is assumed paved")]
        [TestCase("""{"highway":"footway","designation":"public_footpath"}""", "off_road",
            Description = "a field footpath mapped as a footway: OS says unmade for 20.1 of 24.4 km (T174, #178)")]
        [TestCase("""{"highway":"footway","designation":"public_footpath","surface":"asphalt"}""", "traffic_free", Description = "a surface tag still decides")]
        [TestCase("""{"highway":"footway","designation":"public_footpath","footway":"sidewalk"}""", "connector")]
        [TestCase("""{"highway":"footway","footway":"crossing"}""", "traffic_free")]
        [TestCase("""{"highway":"pedestrian"}""", "traffic_free")]
        [TestCase("""{"highway":"cycleway"}""", "traffic_free")]
        [TestCase("""{"highway":"steps"}""", "traffic_free")]
        [TestCase("""{"highway":"footway","surface":"dirt"}""", "off_road")]
        [TestCase("""{"highway":"path"}""", "off_road", Description = "an untagged path, track or bridleway is assumed unsurfaced")]
        [TestCase("""{"highway":"track"}""", "off_road")]
        [TestCase("""{"highway":"bridleway"}""", "off_road")]
        [TestCase("""{"highway":"byway"}""", "off_road", Description = "a byway is a track in law and on the ground (T035)")]
        [TestCase("""{"highway":"motorway"}""", "connector", Description = "kept by the style (T035)")]
        [TestCase("""{"highway":"motorway_link"}""", "connector")]
        [TestCase("""{"highway":"track","surface":"asphalt"}""", "traffic_free")]
        [TestCase("""{"highway":"path","surface":"paving_stones"}""", "traffic_free")]
        [TestCase("""{"highway":"path","surface":"grass"}""", "off_road")]
        public async Task Then_the_tags_give_this_ground(string tags, string ground) =>
            Assert.That(await Ask("classify_ground", tags), Is.EqualTo(ground));
    }

    [TestFixture]
    [NonParallelizable]
    public class Run_access
    {
        [TestCase("""{"highway":"footway"}""", "allowed")]
        [TestCase("""{"highway":"residential","access":"destination"}""", "allowed", Description = "restricts cars, not feet")]
        [TestCase("""{"highway":"service","access":"private"}""", "forbidden")]
        [TestCase("""{"highway":"footway","access":"no"}""", "forbidden")]
        [TestCase("""{"highway":"footway","foot":"no"}""", "forbidden")]
        [TestCase("""{"highway":"footway","foot":"private"}""", "forbidden")]
        [TestCase("""{"highway":"motorway"}""", "forbidden", Description = "no one may walk on a motorway (T035)")]
        [TestCase("""{"highway":"motorway_link"}""", "forbidden")]
        [TestCase("""{"highway":"service","access":"private","foot":"yes"}""", "allowed")]
        [TestCase("""{"highway":"track","access":"no","foot":"permissive"}""", "allowed")]
        [TestCase("""{"highway":"path","access":"private","foot":"designated"}""", "allowed")]
        //  T081: a driveway is private unless its tags say otherwise ("down someone's drive", user).
        [TestCase("""{"highway":"service","service":"driveway"}""", "forbidden", Description = "a private drive")]
        [TestCase("""{"highway":"service","service":"driveway","access":"destination"}""", "forbidden", Description = "visitors only")]
        [TestCase("""{"highway":"service","service":"driveway","foot":"yes"}""", "allowed")]
        [TestCase("""{"highway":"service","service":"driveway","access":"permissive"}""", "allowed")]
        [TestCase("""{"highway":"service","service":"driveway","access":"yes"}""", "allowed")]
        [TestCase("""{"highway":"service","service":"alley"}""", "allowed", Description = "an alley isn't a drive")]
        //  T175 (#179): a way for customers only counts as private.
        [TestCase("""{"highway":"footway","foot":"customers"}""", "forbidden", Description = "a garden centre's path")]
        [TestCase("""{"highway":"service","access":"customers"}""", "forbidden")]
        [TestCase("""{"highway":"path","access":"customers","foot":"designated"}""", "allowed", Description = "a public footpath through it")]
        [TestCase("""{"highway":"path","access":"customers","foot":"yes"}""", "allowed")]
        //  T179 (#183): a public right of way keeps the public's right through a pub garden or a golf club.
        [TestCase("""{"highway":"footway","designation":"public_footpath","foot":"customers"}""", "allowed", Description = "a public footpath through a pub garden")]
        [TestCase("""{"highway":"track","designation":"public_bridleway","access":"customers"}""", "allowed")]
        [TestCase("""{"highway":"track","designation":"byway_open_to_all_traffic","access":"customers"}""", "allowed")]
        [TestCase("""{"highway":"footway","designation":"permissive_footpath","foot":"customers"}""", "forbidden", Description = "permission isn't a right")]
        public async Task Then_a_Run_may_or_may_not_use_it(string tags, string access) =>
            Assert.That(await Ask("run_access", tags), Is.EqualTo(access));
    }

    //  T181 (#185): a road with a real pavement to walk on, 30 mph or under with pavements both sides,
    //  where a Main Road costs a Run less.
    [TestFixture]
    [NonParallelizable]
    public class Pavements
    {
        [TestCase("""{"highway":"primary","maxspeed":"30 mph","sidewalk":"both"}""", "true")]
        [TestCase("""{"highway":"primary","maxspeed":"20 mph","sidewalk":"separate"}""", "true", Description = "pavements mapped as ways of their own")]
        [TestCase("""{"highway":"primary","maxspeed":"30 mph","sidewalk:both":"yes"}""", "true")]
        [TestCase("""{"highway":"primary","maxspeed":"30 mph","sidewalk:left":"yes","sidewalk:right":"separate"}""", "true")]
        [TestCase("""{"highway":"primary","maxspeed":"48","sidewalk":"both"}""", "true", Description = "48 km/h")]
        [TestCase("""{"highway":"primary","maxspeed":"40 mph","sidewalk":"both"}""", "false", Description = "too fast")]
        [TestCase("""{"highway":"primary","maxspeed":"30 mph","sidewalk":"left"}""", "false", Description = "one side only")]
        [TestCase("""{"highway":"primary","maxspeed":"30 mph"}""", "false", Description = "pavements not tagged")]
        [TestCase("""{"highway":"primary","sidewalk":"both"}""", "false", Description = "speed not tagged")]
        public async Task Then_it_has_a_real_pavement_or_not(string tags, string pavement) =>
            Assert.That(await Ask("has_pavement", tags), Is.EqualTo(pavement));
    }

    //  Which land open ground crosses (T039), and which it never does: private land, and since T175
    //  (#179) land for customers only, such as a pub garden or a golf club's.
    [TestFixture]
    [NonParallelizable]
    public class Open_ground_land
    {
        [TestCase("""{"leisure":"park"}""", "open")]
        [TestCase("""{"leisure":"park","access":"private"}""", "blocked")]
        [TestCase("""{"leisure":"park","access":"customers"}""", "blocked")]
        [TestCase("""{"landuse":"grass","access":"customers"}""", "blocked")]
        [TestCase("""{"leisure":"park","foot":"customers"}""", "blocked", Description = "T179: on foot, for customers only")]
        public async Task Then_the_land_is_crossed_or_not(string tags, string kind) =>
            Assert.That(await Ask("open_ground_class", tags), Is.EqualTo(kind));
    }

    //  T068: where a Gravel Ride may go (SPEC R9.1, CONTEXT: Access, Unconfirmed Access). Not on
    //  footpaths or pavements; where the tags can't tell, Unconfirmed.
    [TestFixture]
    [NonParallelizable]
    public class RideAccess
    {
        [TestCase("""{"highway":"residential"}""", "allowed", Description = "roads")]
        [TestCase("""{"highway":"primary"}""", "allowed")]
        [TestCase("""{"highway":"service"}""", "allowed")]
        [TestCase("""{"highway":"cycleway"}""", "allowed")]
        [TestCase("""{"highway":"cycleway","designation":"public_footpath"}""", "allowed", Description = "built for bikes")]
        [TestCase("""{"highway":"bridleway"}""", "allowed")]
        [TestCase("""{"highway":"byway"}""", "allowed")]
        [TestCase("""{"highway":"track"}""", "allowed")]
        [TestCase("""{"highway":"track","designation":"restricted_byway"}""", "allowed")]
        [TestCase("""{"highway":"path","designation":"public_bridleway"}""", "allowed")]
        [TestCase("""{"highway":"footway","bicycle":"yes"}""", "allowed", Description = "a shared path")]
        [TestCase("""{"highway":"path","bicycle":"designated"}""", "allowed")]
        [TestCase("""{"highway":"pedestrian","bicycle":"permissive"}""", "allowed")]
        [TestCase("""{"highway":"service","access":"private","bicycle":"yes"}""", "allowed")]
        [TestCase("""{"highway":"footway"}""", "forbidden", Description = "a pavement or footpath")]
        [TestCase("""{"highway":"footway","footway":"sidewalk"}""", "forbidden")]
        [TestCase("""{"highway":"pedestrian"}""", "forbidden")]
        [TestCase("""{"highway":"steps"}""", "forbidden")]
        [TestCase("""{"highway":"path","designation":"public_footpath"}""", "forbidden")]
        [TestCase("""{"highway":"track","designation":"public_footpath"}""", "forbidden", Description = "a footpath along a farm track")]
        [TestCase("""{"highway":"bridleway","bicycle":"no"}""", "forbidden")]
        [TestCase("""{"highway":"cycleway","bicycle":"dismount"}""", "forbidden")]
        [TestCase("""{"highway":"service","access":"private"}""", "forbidden")]
        [TestCase("""{"highway":"track","access":"no"}""", "forbidden")]
        [TestCase("""{"highway":"motorway"}""", "forbidden")]
        [TestCase("""{"highway":"path"}""", "unconfirmed", Description = "a path the tags can't tell")]
        [TestCase("""{"highway":"path","foot":"designated"}""", "unconfirmed")]
        //  T081: a driveway is private unless its tags say otherwise.
        [TestCase("""{"highway":"service","service":"driveway"}""", "forbidden")]
        [TestCase("""{"highway":"service","service":"driveway","bicycle":"yes"}""", "allowed")]
        [TestCase("""{"highway":"service","service":"driveway","access":"yes"}""", "allowed")]
        //  T175: a way for customers only counts as private.
        [TestCase("""{"highway":"track","bicycle":"customers"}""", "forbidden")]
        [TestCase("""{"highway":"service","access":"customers"}""", "forbidden")]
        [TestCase("""{"highway":"track","access":"customers","bicycle":"permissive"}""", "allowed")]
        //  T179 (#183): a bridleway or byway keeps its riders' right; a footpath never had one.
        [TestCase("""{"highway":"track","designation":"public_bridleway","access":"customers"}""", "allowed")]
        [TestCase("""{"highway":"track","designation":"restricted_byway","bicycle":"customers"}""", "allowed")]
        [TestCase("""{"highway":"footway","designation":"public_footpath","access":"customers"}""", "forbidden")]
        public async Task Then_a_Gravel_Ride_may_or_may_not_use_it(string tags, string access) =>
            Assert.That(await Ask("ride_access", tags), Is.EqualTo(access));
    }
}
