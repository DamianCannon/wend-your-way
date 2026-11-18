using WendYourWay.Cli;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>`wend network build &lt;extract&gt;` (PLAN T005, STORY-002).</summary>
public static class BuildingTheNetworkFromTheCommandLine
{
    private static async Task<(int ExitCode, string Output)> Ct(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    [TestFixture]
    [NonParallelizable]
    public class A_successful_build
    {
        private (int ExitCode, string Output) _run;

        [OneTimeSetUp]
        public async Task Arrange()
        {
            TestDatabase.Wipe();
            _run = await Ct("network", "build", Fixtures.Path("topology.osm"),
                "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"), "--osm2pgsql", Tools.Osm2pgsql);
        }

        [Test]
        public void Then_it_succeeds() => Assert.That(_run.ExitCode, Is.Zero, _run.Output);

        [Test]
        public void Then_it_reports_the_Segments_written() =>
            Assert.That(_run.Output, Does.Match(@"Segments written\s+19\b"));

        [Test]
        public void Then_it_reports_ways_it_had_to_skip() =>
            Assert.That(_run.Output, Does.Match(@"ways skipped \(geometry and nodes disagree\)\s+1\b"));

        [Test]
        public void Then_it_reports_the_paths_it_found_beside_roads() =>
            Assert.That(_run.Output, Does.Match(@"paths beside roads \(same ground\)\s+\d+\b"));

        [Test]
        public void Then_it_reports_the_Open_ground_it_crossed() =>
            Assert.That(_run.Output, Does.Match(@"Open ground crossings\s+\d+\s+\(\d+ km\)"));

        //  #150, T146a: the gaps in the map joined, and kept as possible.
        [Test]
        public void Then_it_reports_the_gaps_in_the_map_it_joined_and_kept() =>
            Assert.That(_run.Output, Does.Match(@"gaps in the map joined \(under 5 m\)\s+\d+").And.Match(@"gaps in the map possible \(5 m to 15 m\)\s+\d+"));

        [Test]
        public void Then_it_says_what_each_phase_did() =>
            Assert.That(_run.Output, Does.Match(@"(?m)^\s*open ground\s+crossed\b"));

        [TestCase("migrations")]
        [TestCase("osm2pgsql")]
        [TestCase("segments")]
        [TestCase("same ground")]
        [TestCase("open ground")]
        [TestCase("off-road networks")]
        [TestCase("trailheads")]
        [TestCase("outings scored")]
        public void Then_it_times_each_phase(string phase) =>
            Assert.That(_run.Output, Does.Match($@"(?m)^\s*{phase}\b.*\d+(\.\d+)? s\s*$"));

        //  T022: green ground joined into Off-road networks, the largest reported.
        [Test]
        public void Then_it_reports_the_Off_road_networks_it_found() =>
            Assert.That(_run.Output, Does.Match(@"Off-road networks\s+\d+\s+\(largest \d+\.\d+ km\)"));

        //  T023: car parks beside the large networks.
        [Test]
        public void Then_it_reports_the_Trailheads_it_found() =>
            Assert.That(_run.Output, Does.Match(@"Trailheads\s+\d+\s+\(of \d+ car parks beside them\)"));

        [Test]
        public async Task Then_every_green_Segment_is_in_an_Off_road_network()
        {
            await using var dataSource = WendYourWay.Data.Database.CreateDataSource(TestDatabase.ConnectionString);
            await using var command = dataSource.CreateCommand("""
                select count(*) filter (where o.network_id is null), count(*)
                  from network.segments s left join network.segment_offroad o on o.segment_id = s.id
                 where s.ground in ('off_road', 'traffic_free') and s.access_run <> 'forbidden'
                """);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            Assert.That((reader.GetInt64(0), reader.GetInt64(1) > 0), Is.EqualTo((0L, true)));
        }

        //  Run on its own on the Network as it is, so a rule is measured without a rebuild.
        [Test]
        public async Task Then_its_Off_road_networks_can_be_found_again_on_their_own()
        {
            var (exitCode, output) = await Ct("network", "offroad", "--database", TestDatabase.ConnectionString);
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Match(@"Off-road networks\s+\d+\s+\(largest \d+\.\d+ km\)"));
        }

        //  A rule changes more often than OSM: rebuild from what's loaded, with no re-import (T036).
        [Test]
        public async Task Then_it_can_be_rebuilt_from_what_was_loaded_without_osm2pgsql()
        {
            var (exitCode, output) = await Ct("network", "rebuild", "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"));
            Assert.That((exitCode, output.Contains("osm2pgsql", StringComparison.Ordinal)), Is.EqualTo((0, false)), output);
            Assert.That(output, Does.Match(@"Segments written\s+19\b"));
        }

        //  The fixture holds no country boundaries, like a regional extract (T030).
        [Test]
        public void Then_it_says_the_Network_isn_t_clipped_and_why() =>
            Assert.That(_run.Output, Does.Contain("not clipped to England and Wales: the extract holds no country whole"));
    }

    //  Ctrl+C: the build stops, in PostgreSQL too, and says the Network is as it was.
    [TestFixture]
    [NonParallelizable]
    public class A_build_that_is_stopped
    {
        [Test]
        public async Task Then_it_stops_and_says_so()
        {
            TestDatabase.Wipe();
            using var output = new StringWriter();
            using var stopped = new CancellationTokenSource();
            await stopped.CancelAsync();
            var exitCode = await CommandLine.Run(
                ["network", "build", Fixtures.Path("same-ground.osm"), "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"), "--osm2pgsql", Tools.Osm2pgsql],
                output, stopped.Token);
            Assert.That((exitCode, output.ToString().Contains("Stopped. The Network is as it was.", StringComparison.Ordinal)), Is.EqualTo((1, true)), output.ToString());
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class A_build_clipped_to_England_and_Wales
    {
        private (int ExitCode, string Output) _run;

        [OneTimeSetUp]
        public async Task Arrange()
        {
            TestDatabase.Wipe();
            _run = await Ct("network", "build", Fixtures.Path("border.osm"),
                "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"), "--osm2pgsql", Tools.Osm2pgsql);
        }

        [Test]
        public void Then_it_reports_what_it_left_out_in_Scotland() =>
            Assert.That(_run.Output, Does.Match(@"Segments skipped \(in Scotland\)\s+2\b"));

        [Test]
        public void Then_it_reports_what_it_left_out_elsewhere() =>
            Assert.That(_run.Output, Does.Match(@"Segments skipped \(in no country of Great Britain\)\s+1\b"));
    }

    [TestFixture]
    [NonParallelizable]
    public class When_the_extract_is_missing
    {
        private (int ExitCode, string Output) _run;

        [OneTimeSetUp]
        public async Task Arrange() =>
            _run = await Ct("network", "build", "no-such-extract.osm.pbf", "--database", TestDatabase.ConnectionString);

        [Test]
        public void Then_it_fails() => Assert.That(_run.ExitCode, Is.Not.Zero);

        [Test]
        public void Then_it_names_the_missing_file() =>
            Assert.That(_run.Output, Does.Contain("no-such-extract.osm.pbf"));
    }

    //  T021: a Network without the byways would have no Trail Rides, and nothing would say so.
    [TestFixture]
    [NonParallelizable]
    public class When_the_Definitive_Map_byways_are_missing
    {
        private (int ExitCode, string Output) _run;

        [OneTimeSetUp]
        public async Task Arrange() =>
            _run = await Ct("network", "rebuild", "--database", TestDatabase.ConnectionString, "--rights-of-way", "no-such-folder");

        [Test]
        public void Then_it_fails() => Assert.That(_run.ExitCode, Is.Not.Zero);

        [Test]
        public void Then_it_says_where_to_get_them() =>
            Assert.That(_run.Output, Does.Contain("rowmaps.com"));
    }

    [TestFixture]
    [NonParallelizable]
    public class A_build_with_the_Definitive_Map_byways
    {
        private (int ExitCode, string Output) _run;

        [OneTimeSetUp]
        public async Task Arrange()
        {
            TestDatabase.Wipe();
            _run = await Ct("network", "build", Fixtures.Path("trail-rides.osm"), "--database", TestDatabase.ConnectionString,
                "--rights-of-way", Fixtures.Path("rights-of-way"), "--osm2pgsql", Tools.Osm2pgsql);
        }

        [Test]
        public void Then_it_succeeds() => Assert.That(_run.ExitCode, Is.Zero, _run.Output);

        [Test]
        public void Then_it_reports_the_byways_it_loaded() =>
            Assert.That(_run.Output, Does.Match(@"Definitive Map byways\s+3\s+\(2 km, 1 authorities\)"));
    }

    //  #152, T148: each phase told as it finishes, by the names an update counts its steps by, so a phase
    //  renamed or added can't leave the count stuck.
    [TestFixture]
    public class A_build_for_an_update
    {
        [Test]
        public async Task Then_each_phase_done_is_one_of_the_update_s_steps_in_order()
        {
            var phases = new List<string>();
            using var output = new StringWriter();
            var exitCode = await NetworkBuildCommand.Run(Fixtures.Path("topology.osm"), TestDatabase.ConnectionString, Tools.Osm2pgsql, terrain: null,
                Fixtures.Path("rights-of-way"), output, phaseDone: phases.Add);
            var steps = WendYourWay.Data.Building.UpdateSteps.For(climbing: false);
            // From loading the map on, each phase is followed by the next: "downloading" comes before.
            var expected = new List<string> { "osm2pgsql" };
            while (expected.Count < phases.Count && steps.After(expected[^1]) is { } next) expected.Add(next);
            Assert.That((exitCode, phases, phases.LastOrDefault()), Is.EqualTo((0, expected, "outings")), output.ToString());
        }
    }

    [TestFixture]
    public class An_unknown_command
    {
        [Test]
        public async Task Then_it_shows_how_to_use_ct()
        {
            var (exitCode, output) = await Ct("fly", "kite");
            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(2));
                Assert.That(output, Does.Contain("wend network build"));
            });
        }
    }
}
