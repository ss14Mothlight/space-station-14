using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Persistence.GridControl;
using Content.Server.Access.Systems;
using Content.Shared._Persistence.CCVar;
using Content.Shared._Persistence.GridControl;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Content.IntegrationTests.Fixtures.Attributes;

namespace Content.IntegrationTests.Tests._Mothlight;

[TestFixture]
[TestOf(typeof(BluespaceParkingSystem))]
public sealed class BluespaceParkingTest : GameTest
{
    private const string Key = "BSPAnchorKey";
    private const string Marker = "Crowbar";

    /// <summary>
    /// An anchor key parks the grid it's on (saving it and taking it out of the world) and brings it back, with
    /// everything that was on it, next to where it was parked.
    /// </summary>
    [Test]
    public async Task ParkAndUnpark()
    {
        await OverrideCVar(Side.Server, PersistenceCCVars.BluespaceParkingParkDelay, 0);
        await OverrideCVar(Side.Server, PersistenceCCVars.BluespaceParkingUnparkDelay, 0);

        var map = await Pair.CreateTestMap();
        var res = Server.ResolveDependency<IResourceManager>();

        EntityUid key = default;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity(Marker, map.GridCoords);
            key = SEntMan.SpawnEntity(Key, map.GridCoords);

            var id = SEntMan.SpawnEntity("PassengerIDCard", MapCoordinates.Nullspace);
            Server.System<IdCardSystem>().TryChangeFullName(id, "Parker Pat");
            var slot = SEntMan.GetComponent<BSPAnchorKeyComponent>(key).PrivilegedIdSlot;
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(key, slot, id, null));

            SEntMan.EventBus.RaiseLocalEvent(key, new BSPAnchorKeyStartPark { UiKey = BSPAnchorKeyUiKey.Key });
            Assert.That(SEntMan.GetComponent<BSPAnchorKeyComponent>(key).State, Is.EqualTo(BSPState.Parking));
        });
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<BSPAnchorKeyComponent>(key);
            Assert.That(comp.State, Is.EqualTo(BSPState.Parked));
            Assert.That(SEntMan.Deleted(map.Grid), "The parked grid should be gone from the world");
            Assert.That(SEntMan.GetComponent<TransformComponent>(key).MapID, Is.EqualTo(map.MapId),
                "The key should have been left behind to bring the grid back");
            Assert.That(res.UserData.Exists(BluespaceParkingSystem.SaveRoot / comp.SavedFilename!));
            Assert.That(CountMarkers(map.MapId), Is.Zero, "What was on the grid went with it");

            SEntMan.EventBus.RaiseLocalEvent(key, new BSPAnchorKeyStartUnpark { UiKey = BSPAnchorKeyUiKey.Key });
            Assert.That(comp.State, Is.EqualTo(BSPState.Unparking));
        });
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<BSPAnchorKeyComponent>(key);
            Assert.That(comp.State, Is.EqualTo(BSPState.Idle));
            Assert.That(comp.SavedFilename, Is.Null);
            Assert.That(CountMarkers(map.MapId), Is.EqualTo(1), "What was on the grid should be back with it");
        });
    }

    private int CountMarkers(MapId map)
    {
        return SEntMan.AllEntities<MetaDataComponent>()
            .Count(e => e.Comp.EntityPrototype?.ID == Marker && SEntMan.GetComponent<TransformComponent>(e).MapID == map);
    }
}
