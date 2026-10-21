using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Persistence.Factions;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Persistence.Cargo;
using Content.Shared.Cargo.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Mothlight;

[TestFixture]
[TestOf(typeof(CargoSystem))]
public sealed class CargoSaleTest : GameTest
{
    private const string Goods = "CargoSaleTestGoods";

    // YAML indentation, not C#.
    // editorconfig-checker-disable
    [TestPrototypes]
    private static readonly string _prototypes = $@"
- type: entity
  parent: BaseItem
  id: {Goods}
  components:
  - type: StaticPrice
    price: 100
";
    // editorconfig-checker-enable

    [SidedDependency(Side.Server)] private readonly FactionSystem _faction = null!;
    [SidedDependency(Side.Server)] private readonly StationSystem _station = null!;

    /// <summary>
    /// Selling deposits into the station that owns the console, or pays out as cash.
    /// </summary>
    [Test]
    public async Task SellGoods()
    {
        var map = await Pair.CreateTestMap();

        EntityUid station = default, console = default, seller = default, goods = default;
        await Server.WaitPost(() =>
        {
            station = _faction.CreateFaction("Sale Faction", null);
            _station.AddGridToStation(station, map.Grid);

            var pallet = SEntMan.SpawnEntity("CargoPalletSell", map.GridCoords);
            Assert.That(SEntMan.GetComponent<TransformComponent>(pallet).Anchored);
            goods = SEntMan.SpawnEntity(Goods, map.GridCoords);
            console = SEntMan.SpawnEntity("ComputerPalletConsole", map.GridCoords);
            seller = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunTicksSync(2);

        var before = 0;
        await Server.WaitAssertion(() =>
        {
            before = _faction.GetFactionBalance(station);
            SEntMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage { Actor = seller });
            Assert.That(SEntMan.Deleted(goods), "The goods on the pallet should have been sold");
            Assert.That(_faction.GetFactionBalance(station), Is.GreaterThan(before), "The station should have been paid");

            goods = SEntMan.SpawnEntity(Goods, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(console, new CargoPalletSetModeMessage(CargoSaleMode.Cash) { Actor = seller });
            SEntMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage { Actor = seller });
            Assert.That(SEntMan.Deleted(goods));
            Assert.That(Server.System<SharedHandsSystem>().TryGetActiveItem(seller, out var cash), "The seller should be handed the cash");
            Assert.That(SEntMan.GetComponent<StackComponent>(cash!.Value).Count, Is.EqualTo(100));
        });
    }

    /// <summary>
    /// A console on a grid no station owns can't deposit to "this station", but can still pay out as cash.
    /// </summary>
    [Test]
    public async Task SellWithoutStation()
    {
        var map = await Pair.CreateTestMap();

        EntityUid console = default, seller = default, goods = default;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("CargoPalletSell", map.GridCoords);
            goods = SEntMan.SpawnEntity(Goods, map.GridCoords);
            console = SEntMan.SpawnEntity("ComputerPalletConsole", map.GridCoords);
            seller = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage { Actor = seller });
            Assert.That(SEntMan.Deleted(goods), Is.False, "There's no station to deposit into");

            SEntMan.EventBus.RaiseLocalEvent(console, new CargoPalletSetModeMessage(CargoSaleMode.Cash) { Actor = seller });
            SEntMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage { Actor = seller });
            Assert.That(SEntMan.Deleted(goods));
            Assert.That(Server.System<SharedHandsSystem>().TryGetActiveItem(seller, out var cash));
            Assert.That(SEntMan.GetComponent<StackComponent>(cash!.Value).Count, Is.EqualTo(100), "No station, no tax");
        });
    }
}
