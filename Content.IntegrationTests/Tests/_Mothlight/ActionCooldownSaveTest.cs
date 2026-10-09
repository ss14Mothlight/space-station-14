using System.IO;
using Content.IntegrationTests.Fixtures;
using Content.Server._Mothlight.Persistence;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Mothlight;

[TestFixture]
[TestOf(typeof(ActionCooldown))]
public sealed class ActionCooldownSaveTest : GameTest
{
    /// <summary>
    /// Action cooldowns are saved relative to the current time. They used to be saved as absolute times, which left
    /// a loaded character's actions on cooldown for however long the previous round had been running.
    /// </summary>
    [Test]
    public async Task CooldownSavedRelative()
    {
        var map = await Pair.CreateTestMap();
        // Get far enough into the round that an absolute time would stand out.
        await RunTicksSync(300);

        await Server.WaitAssertion(() =>
        {
            var actions = Server.System<SharedActionsSystem>();
            var mob = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var proto = "Jump";
            var action = actions.AddAction(mob, proto)!.Value;
            actions.SetCooldown(action, TimeSpan.FromSeconds(30));

            var writer = new StringWriter();
            using (Server.System<CharacterPersistenceSystem>().AllowHumanoidSaving())
                Assert.That(Server.System<MapLoaderSystem>().TrySaveEntity(mob, writer));

            var text = writer.ToString();
            Assert.That(text, Does.Contain("start: 0\n"));
            Assert.That(text, Does.Contain("end: 30\n"));
        });
    }
}
