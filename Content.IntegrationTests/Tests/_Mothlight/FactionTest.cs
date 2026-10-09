using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Mothlight.Persistence;
using Content.Server._Persistence.Factions;
using Content.Server.Access.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Presets;
using Content.Server.Station.Systems;
using Content.Shared._Persistence.Factions;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Mothlight;

[TestFixture]
[TestOf(typeof(FactionSystem))]
public sealed class FactionTest : GameTest
{
    private const string Owner = "Owner Olive";
    private const string Member = "Member Mike";
    private const string Stranger = "Stranger Sam";
    private const string TaggedReader = "FactionTestTaggedReader";
    private const string OpenReader = "FactionTestOpenReader";
    private const string IdCard = "AssistantIDCard";

    // YAML indentation, not C#.
    // editorconfig-checker-disable
    [TestPrototypes]
    private static readonly string _prototypes = $@"
- type: entity
  id: {TaggedReader}
  components:
  - type: AccessReader
    access: [[""Engineering""]]

- type: entity
  id: {OpenReader}
  components:
  - type: AccessReader
";
    // editorconfig-checker-enable

    [SidedDependency(Side.Server)] private readonly FactionSystem _faction = null!;
    [SidedDependency(Side.Server)] private readonly FactionAccessSystem _factionAccess = null!;
    [SidedDependency(Side.Server)] private readonly AccessReaderSystem _accessReader = null!;
    [SidedDependency(Side.Server)] private readonly AccessSystem _access = null!;
    [SidedDependency(Side.Server)] private readonly IdCardSystem _idCard = null!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = null!;
    [SidedDependency(Side.Server)] private readonly StationSystem _station = null!;

    /// <summary>
    /// On a faction's grid, membership opens doors on top of the access levels on someone's ID: owners get in
    /// everywhere, assignments grant access levels and the faction's own accesses, and IDs still work as normal.
    /// </summary>
    [Test]
    public async Task FactionAccessIsAdditive()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var station = _faction.CreateFaction("Test Faction", Owner);
            _station.AddGridToStation(station, map.Grid);

            var assignments = SEntMan.GetComponent<CrewAssignmentsComponent>(station);
            var engineer = assignments.CreateAssignment("Engineer");
            engineer.AccessIDs.Add("Engineering");
            var guard = assignments.CreateAssignment("Guard");
            guard.AccessIDs.Add("Vault");
            SEntMan.GetComponent<CrewAccessesComponent>(station).CrewAccesses.Add("Vault");

            var tagged = SEntMan.SpawnEntity(TaggedReader, map.GridCoords);
            var open = SEntMan.SpawnEntity(OpenReader, map.GridCoords);
            _factionAccess.ToggleAccess(open, "Vault");

            var owner = SpawnHolding(map.GridCoords, Owner);
            var member = SpawnHolding(map.GridCoords, Member);
            var stranger = SpawnHolding(map.GridCoords, Stranger);

            Assert.Multiple(() =>
            {
                Assert.That(_accessReader.IsAllowed(owner, tagged), "Owners get in everywhere on their grids");
                Assert.That(_accessReader.IsAllowed(owner, open));
                Assert.That(_accessReader.IsAllowed(member, tagged), Is.False, "No record, no access");
                Assert.That(_accessReader.IsAllowed(member, open), Is.False,
                    "A reader with faction requirements isn't open to everyone just because it has no access levels");
                Assert.That(_accessReader.IsAllowed(stranger, tagged), Is.False);
            });

            var record = _faction.EnsureRecord(station, Member)!;
            _faction.SetAssignment(station, record, engineer.ID);
            Assert.Multiple(() =>
            {
                Assert.That(_accessReader.IsAllowed(member, tagged), "Assignments grant normal access levels");
                Assert.That(_accessReader.IsAllowed(member, open), Is.False);
            });

            _faction.SetAssignment(station, record, guard.ID);
            Assert.Multiple(() =>
            {
                Assert.That(_accessReader.IsAllowed(member, tagged), Is.False);
                Assert.That(_accessReader.IsAllowed(member, open), "Assignments grant the faction's own accesses");
            });

            // Access written onto an ID still works, whatever the faction thinks.
            _hands.TryGetActiveItem(stranger, out var strangerId);
            _access.TrySetTags(strangerId!.Value, ["Engineering"]);
            Assert.That(_accessReader.IsAllowed(stranger, tagged), "IDs keep working on faction grids");

            // Personal access lets named people in, faction or not.
            _factionAccess.SetPersonalMode(open, true);
            _factionAccess.AddPersonalAccess(open, Stranger);
            Assert.That(_accessReader.IsAllowed(stranger, open));
        });
    }

    /// <summary>
    /// The ID card console's faction tab works on whoever's ID is in the target slot, with the permissions of whoever's
    /// ID is in the privileged slot, the same as the ID config tab.
    /// </summary>
    [Test]
    public async Task ConsoleFactionTabTargetsTargetId()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var consoles = Server.System<FactionIdCardConsoleSystem>();
            var slots = Server.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>();

            var station = _faction.CreateFaction("Console Faction", Owner);
            _station.AddGridToStation(station, map.Grid);
            var guard = SEntMan.GetComponent<CrewAssignmentsComponent>(station).CreateAssignment("Guard");

            var console = SEntMan.SpawnEntity("ComputerId", map.GridCoords);
            var consoleComp = SEntMan.GetComponent<Content.Shared.Access.Components.IdCardConsoleComponent>(console);

            var ownerId = SEntMan.SpawnEntity(IdCard, map.GridCoords);
            _idCard.TryChangeFullName(ownerId, Owner);
            var memberId = SEntMan.SpawnEntity(IdCard, map.GridCoords);
            _idCard.TryChangeFullName(memberId, Member);

            // Only the privileged ID: nobody to edit, and it mustn't edit its own holder.
            Assert.That(slots.TryInsert(console, consoleComp.PrivilegedIdSlot, ownerId, null));
            var state = consoles.BuildState((console, consoleComp));
            Assert.That(state.IsOwner);
            Assert.That(state.SelectedName, Is.Null);

            Assert.That(slots.TryInsert(console, consoleComp.TargetIdSlot, memberId, null));
            state = consoles.BuildState((console, consoleComp));
            Assert.That(state.SelectedName, Is.EqualTo(Member));
            Assert.That(state.AssignableIds, Does.Contain(guard.ID), "The owner can assign anyone");

            // With the roles swapped, someone without an assignment can't assign anything.
            slots.TryEject(console, consoleComp.PrivilegedIdSlot, null, out _);
            slots.TryEject(console, consoleComp.TargetIdSlot, null, out _);
            Assert.That(slots.TryInsert(console, consoleComp.PrivilegedIdSlot, memberId, null));
            Assert.That(slots.TryInsert(console, consoleComp.TargetIdSlot, ownerId, null));
            state = consoles.BuildState((console, consoleComp));
            Assert.That(state.IsMember, Is.False, "No record yet, so no access to the faction tab");
        });
    }

    private EntityUid SpawnHolding(Robust.Shared.Map.EntityCoordinates coords, string name)
    {
        var mob = SEntMan.SpawnEntity("MobHuman", coords);
        var id = SEntMan.SpawnEntity(IdCard, coords);
        _idCard.TryChangeFullName(id, name);
        _access.TrySetTags(id, []);
        Assert.That(_hands.TryPickupAnyHand(mob, id));
        return mob;
    }
}

[TestFixture]
[TestOf(typeof(FactionSystem))]
public sealed class FactionPersistenceTest : GameTest
{
    private const string Map = "FactionPersistenceTestMap";
    private const string Owner = "Owner Olive";
    private const string Member = "Member Mike";

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

    [SidedDependency(Side.Server)] private readonly FactionSystem _faction = null!;

    /// <summary>
    /// A faction founded mid-round, with no grids to its name, comes back with the rest of the world.
    /// </summary>
    [Test]
    public async Task FactionSurvivesRoundRestart()
    {
        var ticker = Server.System<GameTicker>();
        Server.CfgMan.SetCVar(CCVars.GameMap, Map);
        await Server.WaitPost(() => ticker.SetGamePreset(SProtoMan.Index<GamePresetPrototype>(Preset)));
        await Server.WaitPost(() => ticker.StartRound());
        await RunTicksSync(10);

        var uid = 0;
        await Server.WaitAssertion(() =>
        {
            var station = _faction.CreateFaction("Saved Faction", Owner);
            var data = SEntMan.GetComponent<FactionDataComponent>(station);
            uid = data.UID;

            var assignment = SEntMan.GetComponent<CrewAssignmentsComponent>(station).CreateAssignment("Guard", wage: 25);
            assignment.AccessIDs.Add("Vault");
            SEntMan.GetComponent<CrewAccessesComponent>(station).CrewAccesses.Add("Vault");
            var record = _faction.EnsureRecord(station, Member)!;
            _faction.SetAssignment(station, record, assignment.ID);
            record.CriminalRecord = "Jaywalking";
            _faction.EnsureMetaRecord(Member)!.Level = "NetworkLevel2";

            ticker.EndRound();
        });
        await RunTicksSync(10);

        await Server.WaitPost(() => ticker.RestartRound());
        await RunTicksSync(10);
        await Server.WaitPost(() => ticker.StartRound());
        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.AllEntities<WorldPersistenceRuleComponent>().Single().Comp.LoadedFromSave);

            var station = _faction.GetStationByID(uid);
            Assert.That(station, Is.Not.Null, "The faction should have been saved and loaded with the world");
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(station!.Value).EntityName, Is.EqualTo("Saved Faction"));
            Assert.That(_faction.IsOwner(Owner, station.Value));

            Assert.That(_faction.TryGetAssignment(Member, station.Value, out var assignment));
            Assert.That(assignment!.Name, Is.EqualTo("Guard"));
            Assert.That(assignment.Wage, Is.EqualTo(25));
            Assert.That(_faction.GetGrantedAccesses(Member, station.Value), Does.Contain("Vault"));
            Assert.That(SEntMan.GetComponent<CrewAccessesComponent>(station.Value).CrewAccesses, Does.Contain("Vault"));
            Assert.That(_faction.TryGetRecord(Member, station.Value, out var record));
            Assert.That(record!.CriminalRecord, Is.EqualTo("Jaywalking"));

            Assert.That(_faction.TryGetMetaRecord(Member, out var meta));
            Assert.That(meta!.Level.Id, Is.EqualTo("NetworkLevel2"));

            // New factions don't reuse the loaded one's id.
            var second = _faction.CreateFaction("Second Faction", Owner);
            Assert.That(SEntMan.GetComponent<FactionDataComponent>(second).UID, Is.GreaterThan(uid));
        });

        await Server.WaitPost(() => ticker.RestartRound());
    }
}
