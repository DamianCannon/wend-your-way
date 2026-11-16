using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// A map update's progress counted in steps (#152, T148; user: "maybe it should also say "step 2/8" or
/// something like that?"; chose every phase counted): each of the build's phases a step of its own, so
/// the count moves every ten minutes to an hour; and a step that doesn't apply this time not counted.
/// </summary>
public static class CountingAnUpdatesSteps
{
    [TestFixture]
    public class Given_an_update_with_terrain_to_climb_on
    {
        private static readonly UpdateSteps Steps = UpdateSteps.For(climbing: true);

        [Test]
        public void The_first_step_is_checking_for_a_newer_map() =>
            Assert.That(Steps.Said("checking"), Is.EqualTo("step 1 of 17, checking for a newer map"));

        [Test]
        public void Each_phase_of_the_build_is_a_step_of_its_own() =>
            Assert.That(Steps.Said("climbing"), Is.EqualTo("step 12 of 17, working out the climbing"));

        [Test]
        public void The_last_step_is_the_backup() =>
            Assert.That(Steps.Said("backing up"), Is.EqualTo("step 17 of 17, backing up"));

        [Test]
        public void A_phase_done_is_followed_by_the_next() =>
            Assert.That(Steps.After("gravel networks"), Is.EqualTo("climbing"));

        [Test]
        public void Loading_the_map_is_followed_by_the_rights_of_way() =>
            Assert.That(Steps.After("osm2pgsql"), Is.EqualTo("rights of way"));

        [TestCase("migrations")]
        [TestCase("backing up")]
        public void After_what_isn_t_a_step_or_the_last_comes_nothing(string phase) =>
            Assert.That(Steps.After(phase), Is.Null);
    }

    [TestFixture]
    public class Given_an_update_with_no_terrain
    {
        private static readonly UpdateSteps Steps = UpdateSteps.For(climbing: false);

        [Test]
        public void The_climbing_isn_t_a_step() => Assert.That(Steps.Said("climbing"), Is.Null);

        [Test]
        public void So_the_count_still_ends_at_its_last_step() =>
            Assert.That(Steps.Said("backing up"), Is.EqualTo("step 16 of 16, backing up"));

        [Test]
        public void The_gravel_networks_are_followed_by_putting_the_paths_in_order() =>
            Assert.That(Steps.After("gravel networks"), Is.EqualTo("in order of place"));
    }
}
