using System.Linq;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Fixtures;
using Content.Server.CrewManifest;
using Content.Server.GameTicking.Presets;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Server._Mothlight.Persistence;
using Content.Shared.CCVar;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry;
using Content.Shared.CriminalRecords;
using Content.Shared.Preferences;
using Content.Shared.StationRecords;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Mothlight;

[TestFixture]
[TestOf(typeof(WorldPersistenceRuleSystem))]
public sealed class WorldPersistenceTest : GameTest
{
    private const string Map = "WorldPersistenceTestMap";
    private const string Marker = "Crowbar";
    private const string RecordName = "Rec Ord";
    private const string Smeller = "MobVulpkanin";
    private const string Bottle = "DrinkWaterBottleFull";
    private const string Cartridge = "CartridgePistolPractice";
    // A plain string: the YAML linter can't validate server-only prototype kinds referenced from tests.
    private static readonly string Preset = "Persistence";

    // YAML indentation, not C#.
    // editorconfig-checker-disable
    [TestPrototypes]
    private static readonly string _prototypes = $@"
- type: gameMap
  id: {Map}
  mapName: {Map}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            Passenger: [ -1, -1 ]
";
    // editorconfig-checker-enable

    // Dirty: runs and restarts real rounds.
    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        DummyTicker = false,
        Connected = true,
        InLobby = true,
    };

    [SidedDependency(Side.Server)] private readonly GameTicker _ticker = null!;
    [SidedDependency(Side.Server)] private readonly StationSystem _station = null!;
    [SidedDependency(Side.Server)] private readonly SharedMapSystem _map = null!;
    [SidedDependency(Side.Server)] private readonly StationRecordsSystem _records = null!;

    /// <summary>
    /// A round on the persistence preset saves its map at round end, and the next round loads it instead of a
    /// fresh map, along with its station.
    /// </summary>
    [Test]
    public async Task WorldSurvivesRoundRestart()
    {
        Server.CfgMan.SetCVar(CCVars.GameMap, Map);
        await Server.WaitPost(() => _ticker.SetGamePreset(SProtoMan.Index<GamePresetPrototype>(Preset)));

        // First round: fresh map. Leave something behind on the station.
        await Server.WaitPost(() => _ticker.StartRound());
        await RunTicksSync(10);

        EntityUid marker = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(GetRule().LoadedFromSave, Is.False);

            var station = _station.GetStations().Single();
            var grid = _station.GetLargestGrid(station);
            Assert.That(grid, Is.Not.Null);
            marker = SEntMan.SpawnEntity(Marker, new EntityCoordinates(grid!.Value, 0.5f, 0.5f));

            // A mob that adds an action on component init, which used to crash loading.
            SEntMan.SpawnEntity(Smeller, new EntityCoordinates(grid.Value, 1.5f, 0.5f));

            // Its liquid colour is appearance data, which isn't saved.
            var bottle = SEntMan.SpawnEntity(Bottle, new EntityCoordinates(grid.Value, 2.5f, 0.5f));
            Assert.That(SEntMan.HasComponent<SolutionContainerVisualsComponent>(bottle));

            // Whether a cartridge is spent is saved, but how it looks isn't.
            var cartridge = SEntMan.SpawnEntity(Cartridge, new EntityCoordinates(grid.Value, 3.5f, 0.5f));
            SEntMan.GetComponent<CartridgeAmmoComponent>(cartridge).Spent = true;

            // Station records are keyed by type internally, which used to make the station unsaveable.
            var key = _records.AddRecordEntry(station, new GeneralStationRecord { Name = RecordName });
            _records.AddRecordEntry(key, new CriminalRecord());

            // Ending the round saves the world.
            _ticker.EndRound();
        });
        await RunTicksSync(10);

        await Server.WaitPost(() => _ticker.RestartRound());
        await RunTicksSync(10);
        Assert.That(SEntMan.Deleted(marker), "The old round's map should be gone after a restart");

        // Second round: loaded from the save.
        await Server.WaitPost(() => _ticker.StartRound());
        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(GetRule().LoadedFromSave);
            Assert.That(_map.IsInitialized(_ticker.DefaultMap));

            var markers = SEntMan.AllEntities<MetaDataComponent>()
                .Where(e => e.Comp.EntityPrototype?.ID == Marker
                            && SEntMan.GetComponent<TransformComponent>(e).MapID == _ticker.DefaultMap)
                .ToList();
            Assert.That(markers, Has.Count.EqualTo(1), "The item left behind last round should still be there");
            Assert.That(CountOnDefaultMap(Smeller), Is.EqualTo(1), "The mob left behind last round should still be there");
        });

        // Solution visuals get refreshed the tick after loading.
        await RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            var bottle = SEntMan.AllEntities<MetaDataComponent>()
                .Single(e => e.Comp.EntityPrototype?.ID == Bottle
                             && SEntMan.GetComponent<TransformComponent>(e).MapID == _ticker.DefaultMap);
            Assert.That(Server.System<SharedAppearanceSystem>()
                .TryGetData<Color>(bottle, SolutionContainerVisuals.Color, out _),
                "A loaded solution container should have its liquid colour set");

            var cartridge = SEntMan.AllEntities<CartridgeAmmoComponent>()
                .Single(e => SEntMan.GetComponent<TransformComponent>(e).MapID == _ticker.DefaultMap);
            Assert.That(cartridge.Comp.Spent);
            Assert.That(Server.System<SharedAppearanceSystem>()
                    .TryGetData<bool>(cartridge, AmmoVisuals.Spent, out var spent) && spent,
                "A loaded spent cartridge should look spent");

            var station = _station.GetStations().Single();
            var grid = _station.GetLargestGrid(station);
            Assert.That(grid, Is.Not.Null, "The loaded station should still own its grid");
            Assert.That(SEntMan.GetComponent<TransformComponent>(grid!.Value).MapID, Is.EqualTo(_ticker.DefaultMap));

            // On a persistent world, characters come back where they were saved rather than at a spawn point.
            var characters = Server.System<CharacterPersistenceSystem>();
            var user = new NetUserId(Guid.NewGuid());
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Placed Pat");
            // The test map's only floor tile.
            var savedAt = new EntityCoordinates(grid.Value, -0.5f, -0.5f);
            var mob = Server.System<StationSpawningSystem>().SpawnPlayerMob(savedAt, null, profile, null);
            Assert.That(characters.TrySaveCharacter(mob, user, profile.Name));
            SEntMan.DeleteEntity(mob);

            var fallback = new EntityCoordinates(_map.GetMap(_ticker.DefaultMap), 500f, 500f);
            Assert.That(characters.TryLoadCharacter(user, profile, fallback, null, out var loaded, preferSavedPosition: true));
            var xform = SEntMan.GetComponent<TransformComponent>(loaded!.Value);
            Assert.That(xform.GridUid, Is.EqualTo(grid.Value), "The character should be back on the grid it was saved on");
            Assert.That((xform.LocalPosition - savedAt.Position).Length(), Is.LessThan(0.01f),
                "The character should be back where it was saved");
            SEntMan.DeleteEntity(loaded.Value);

            var general = _records.GetRecordsOfType<GeneralStationRecord>(station).ToList();
            Assert.That(general.Select(r => r.Item2.Name), Does.Contain(RecordName));
            var (id, _) = general.First(r => r.Item2.Name == RecordName);
            Assert.That(_records.GetRecordsOfType<CriminalRecord>(station).Select(r => r.Item1), Does.Contain(id));

            // The manifest is built from the records, but used to only get built when one was created or changed.
            var (_, manifest) = Server.System<CrewManifestSystem>().GetCrewManifest(station);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest!.Entries.Select(e => e.Name), Does.Contain(RecordName));
        });

        await Server.WaitPost(() => _ticker.RestartRound());
    }

    /// <summary>
    /// A save that fails to load must not take the round down, or get overwritten by the fresh map.
    /// </summary>
    [Test]
    public async Task BrokenSaveFallsBackWithoutOverwriting()
    {
        const string garbage = "this is: [not a valid map";
        var res = Server.ResolveDependency<IResourceManager>();
        var path = new WorldPersistenceRuleComponent().SavePath;

        // The load failure is logged as an error on purpose.
        await OverrideCVar(Side.Server, RTCVars.FailureLogLevel, LogLevel.Fatal);

        Server.CfgMan.SetCVar(CCVars.GameMap, Map);
        await Server.WaitPost(() =>
        {
            res.UserData.CreateDir(path.Directory);
            using (var file = res.UserData.OpenWriteText(path))
            {
                file.Write(garbage);
            }

            _ticker.SetGamePreset(SProtoMan.Index<GamePresetPrototype>(Preset));
            _ticker.StartRound();
        });
        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            var rule = GetRule();
            Assert.That(rule.LoadedFromSave, Is.False);
            Assert.That(rule.SavingDisabled);
            Assert.That(_station.GetStations(), Is.Not.Empty, "A fresh map should have been generated instead");

            Assert.That(Server.System<WorldPersistenceRuleSystem>().TrySaveActiveWorlds(), Is.False);
            using var reader = res.UserData.OpenText(path);
            Assert.That(reader.ReadToEnd(), Is.EqualTo(garbage), "The broken save must not be overwritten");
        });

        await Server.WaitPost(() =>
        {
            res.UserData.Delete(path);
            _ticker.RestartRound();
        });
    }

    private int CountOnDefaultMap(string proto)
    {
        return SEntMan.AllEntities<MetaDataComponent>()
            .Count(e => e.Comp.EntityPrototype?.ID == proto
                        && SEntMan.GetComponent<TransformComponent>(e).MapID == _ticker.DefaultMap);
    }

    private WorldPersistenceRuleComponent GetRule()
    {
        var rules = SEntMan.AllEntities<WorldPersistenceRuleComponent>().ToList();
        Assert.That(rules, Has.Count.EqualTo(1));
        return rules[0].Comp;
    }
}
