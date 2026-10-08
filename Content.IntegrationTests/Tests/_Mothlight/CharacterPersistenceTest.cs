using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Station.Systems;
using Content.Server._Mothlight.Persistence;
using Content.Shared.Atmos;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Speech.Components;
using Content.Shared.Timing;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._Mothlight;

[TestFixture]
[TestOf(typeof(CharacterPersistenceSystem))]
public sealed class CharacterPersistenceTest : GameTest
{
    private const string Jumpsuit = "ClothingUniformJumpsuitColorGrey";
    private const string PocketItem = "Pen";
    private static readonly Regex EndTimeRegex = new(@"endTime: (-?[0-9.]+)");

    [Test]
    public async Task SaveAndLoadCharacter()
    {
        var map = await Pair.CreateTestMap();
        var persistence = Server.System<CharacterPersistenceSystem>();
        var spawning = Server.System<StationSpawningSystem>();
        var inventory = Server.System<InventorySystem>();
        var mind = Server.System<SharedMindSystem>();
        var loader = Server.System<MapLoaderSystem>();

        var user = new NetUserId(Guid.NewGuid());
        HumanoidCharacterProfile profile = null;

        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Persistent Pete");
            mob = spawning.SpawnPlayerMob(map.GridCoords, null, profile, null);
            var m = mind.CreateMind(null, profile.Name);
            mind.TransferTo(m, mob);

            // Swap out whatever the species spawned with for known items, including a pocket item
            // (pockets depend on the jumpsuit, so this also checks the transfer ordering).
            if (inventory.TryUnequip(mob, "jumpsuit", out var old, force: true))
                SEntMan.DeleteEntity(old);
            var suit = SEntMan.SpawnEntity(Jumpsuit, map.GridCoords);
            var pen = SEntMan.SpawnEntity(PocketItem, map.GridCoords);
            Assert.That(inventory.TryEquip(mob, suit, "jumpsuit", force: true));
            Assert.That(inventory.TryEquip(mob, pen, "pocket1", force: true));
        });
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            // Player-controlled mobs must stay out of regular map saves.
            var mapWriter = new StringWriter();
            Assert.That(loader.TrySaveMap(map.MapUid, mapWriter));
            Assert.That(mapWriter.ToString(), Does.Not.Contain("Persistent Pete"));

            Assert.That(persistence.TrySaveCharacter(mob, user, profile.Name));
            Assert.That(persistence.HasSave(user, profile.Name));
            SEntMan.DeleteEntity(mob);
        });
        await RunTicksSync(5);

        // Same species, edited appearance: the saved body is reused but takes the new look.
        var recoloured = profile.WithCharacterAppearance(profile.Appearance.WithEyeColor(Color.Red));
        await Server.WaitAssertion(() =>
        {
            Assert.That(persistence.TryLoadCharacter(user, recoloured, map.GridCoords, null, out var loaded));
            var uid = loaded!.Value;

            Assert.That(SEntMan.GetComponent<HumanoidAppearanceComponent>(uid).EyeColor, Is.EqualTo(Color.Red));
            Assert.That(SEntMan.GetComponent<VocalComponent>(uid).EmoteSounds, Is.Not.Null,
                "Loaded characters should have their emote sounds picked");
            AssertSlot(uid, "jumpsuit", Jumpsuit);
            AssertSlot(uid, "pocket1", PocketItem);
            SEntMan.DeleteEntity(uid);
        });
        await RunTicksSync(5);

        // Different species: a new body is built and the belongings are moved onto it.
        var lizard = profile.WithSpecies("Reptilian");
        await Server.WaitAssertion(() =>
        {
            Assert.That(persistence.TryLoadCharacter(user, lizard, map.GridCoords, null, out var loaded));
            var uid = loaded!.Value;

            Assert.That(SEntMan.GetComponent<HumanoidAppearanceComponent>(uid).Species.Id, Is.EqualTo("Reptilian"));
            AssertSlot(uid, "jumpsuit", Jumpsuit);
            AssertSlot(uid, "pocket1", PocketItem);
            SEntMan.DeleteEntity(uid);
        });
    }

    [Test]
    public async Task LoadedCharacterCanBreathe()
    {
        var map = await Pair.CreateTestMap();
        var persistence = Server.System<CharacterPersistenceSystem>();
        var spawning = Server.System<StationSpawningSystem>();
        var mind = Server.System<SharedMindSystem>();
        var atmos = Server.System<AtmosphereSystem>();

        var user = new NetUserId(Guid.NewGuid());
        HumanoidCharacterProfile profile = null;
        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            atmos.SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));

            profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Breathing Bill");
            mob = spawning.SpawnPlayerMob(map.GridCoords, null, profile, null);
            mind.TransferTo(mind.CreateMind(null, profile.Name), mob);
        });
        await RunSeconds(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(persistence.TrySaveCharacter(mob, user, profile.Name));
            SEntMan.DeleteEntity(mob);
            Assert.That(persistence.TryLoadCharacter(user, profile, map.GridCoords, null, out var loaded));
            mob = loaded!.Value;
            mind.TransferTo(mind.CreateMind(null, profile.Name), mob);
        });
        await RunSeconds(20);

        await Server.WaitAssertion(() =>
        {
            // Loaded entities have to rebuild their solution caches, or the lungs silently stop working.
            Assert.That(SEntMan.GetComponent<RespiratorComponent>(mob).Saturation,
                Is.GreaterThan(SEntMan.GetComponent<RespiratorComponent>(mob).SuffocationThreshold));
        });
    }

    /// <summary>
    /// Cooldowns are absolute round times. Saved as-is they'd still be "on cooldown" for however long the old round
    /// had been running when loaded into a new round, so they must be saved relative to the current time.
    /// </summary>
    [Test]
    public async Task UseDelaySavedRelative()
    {
        var map = await Pair.CreateTestMap();
        var persistence = Server.System<CharacterPersistenceSystem>();
        var spawning = Server.System<StationSpawningSystem>();
        var hands = Server.System<SharedHandsSystem>();
        var useDelay = Server.System<UseDelaySystem>();
        var res = Server.ResolveDependency<IResourceManager>();

        var user = new NetUserId(Guid.NewGuid());
        HumanoidCharacterProfile profile = null;

        // Get the round clock well past the delay length, so absolute and relative times can't be confused.
        await RunSeconds(10);

        await Server.WaitAssertion(() =>
        {
            profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Cooldown Carl");
            var mob = spawning.SpawnPlayerMob(map.GridCoords, null, profile, null);
            var box = SEntMan.SpawnEntity("BoxSurvival", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(mob, box, checkActionBlocker: false));

            useDelay.SetLength(box, TimeSpan.FromSeconds(2));
            Assert.That(useDelay.TryResetDelay(box));
            Assert.That(persistence.TrySaveCharacter(mob, user, profile.Name));

            using var reader = res.UserData.OpenText(persistence.GetSavePath(user, profile.Name));
            var yaml = reader.ReadToEnd();
            var ends = EndTimeRegex.Matches(yaml);
            Assert.That(ends, Is.Not.Empty);
            foreach (Match end in ends)
            {
                // TimeSpans are written as plain seconds.
                var seconds = double.Parse(end.Groups[1].Value, CultureInfo.InvariantCulture);
                Assert.That(seconds, Is.LessThanOrEqualTo(2),
                    "Use delay end time was saved as an absolute round time");
            }

            SEntMan.DeleteEntity(mob);
        });
    }

    private void AssertSlot(EntityUid uid, string slot, string expected)
    {
        var inventory = Server.System<InventorySystem>();
        Assert.That(inventory.TryGetSlotEntity(uid, slot, out var item), $"Nothing in {slot}");
        Assert.That(SEntMan.GetComponent<MetaDataComponent>(item!.Value).EntityPrototype!.ID, Is.EqualTo(expected));
    }
}
