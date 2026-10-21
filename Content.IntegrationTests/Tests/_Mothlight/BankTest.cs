using Content.IntegrationTests.Fixtures;
using Content.Server._Persistence.Bank;
using Content.Server.Access.Systems;
using Content.Shared._Persistence.Bank;
using Content.Shared._Persistence.Invoices;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Mothlight;

[TestFixture]
[TestOf(typeof(BankSystem))]
public sealed class BankTest : GameTest
{
    private const string Atm = "ComputerBankATM";
    private const string Cash = "SpaceCash";

    /// <summary>
    /// Bank accounts belong to characters, not players, and can be paid into while nobody's playing them.
    /// </summary>
    [Test]
    public async Task AccountsArePerCharacter()
    {
        var map = await Pair.CreateTestMap();
        var bank = Server.System<BankSystem>();

        EntityUid first = default, second = default;
        await Server.WaitPost(() =>
        {
            first = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            second = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Server.System<MetaDataSystem>().SetEntityName(first, "First Fred");
            Server.System<MetaDataSystem>().SetEntityName(second, "Second Sue");
            Server.PlayerMan.SetAttachedEntity(ServerSession!, first);
        });
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(bank.GetBalance(first), Is.EqualTo(BankSystem.StartingBalance));
            Assert.That(SEntMan.GetComponent<BankAccountComponent>(first).Balance, Is.EqualTo(BankSystem.StartingBalance),
                "The player should see their character's balance");
            Assert.That(bank.TryBankDeposit(first, 50));
            Assert.That(bank.TryBankWithdraw(first, 1000), Is.False, "Can't spend more than you have");

            // Someone not being played still gets paid.
            Assert.That(bank.TryBankDeposit("Second Sue", 25));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, second);
        });
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(bank.GetBalance(second), Is.EqualTo(25), "Their account was opened when they got paid");
            Assert.That(bank.GetBalance(first), Is.EqualTo(BankSystem.StartingBalance + 50),
                "Switching characters shouldn't carry money over");
        });
    }

    /// <summary>
    /// ATMs hand out spesos from your account, and take them back.
    /// </summary>
    [Test]
    public async Task AtmWithdrawAndDeposit()
    {
        var map = await Pair.CreateTestMap();
        var bank = Server.System<BankSystem>();

        EntityUid user = default, atm = default;
        await Server.WaitPost(() =>
        {
            user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Server.System<MetaDataSystem>().SetEntityName(user, "Cash Carl");
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            atm = SEntMan.SpawnEntity(Atm, map.GridCoords);
        });
        await RunTicksSync(2);

        EntityUid cash = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(bank.GetBalance(user), Is.EqualTo(BankSystem.StartingBalance), "They should start with an account");
            SEntMan.EventBus.RaiseLocalEvent(atm, new BankWithdrawMessage(60) { Actor = user });
            Assert.That(bank.GetBalance(user), Is.EqualTo(BankSystem.StartingBalance - 60));

            Assert.That(Server.System<SharedHandsSystem>().TryGetActiveItem(user, out var held),
                "The withdrawn cash should be in their hands");
            cash = held!.Value;
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(cash).EntityPrototype?.ID, Is.EqualTo(Cash));
            Assert.That(SEntMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(60));

            var slot = SEntMan.GetComponent<BankATMComponent>(atm).CashSlot;
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(atm, slot, cash, user));
            SEntMan.EventBus.RaiseLocalEvent(atm, new BankDepositMessage { Actor = user });
            Assert.That(bank.GetBalance(user), Is.EqualTo(BankSystem.StartingBalance));
            Assert.That(slot.HasItem, Is.False, "The deposited cash should be gone");
        });
    }

    /// <summary>
    /// Paying an invoice personally moves money from the payer's account to whoever it's paid to.
    /// </summary>
    [Test]
    public async Task PayInvoicePersonally()
    {
        var map = await Pair.CreateTestMap();
        var bank = Server.System<BankSystem>();

        EntityUid payer = default, invoice = default;
        await Server.WaitPost(() =>
        {
            payer = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Server.System<MetaDataSystem>().SetEntityName(payer, "Payer Pam");
            var id = SEntMan.SpawnEntity("PassengerIDCard", map.GridCoords);
            Server.System<IdCardSystem>().TryChangeFullName(id, "Payer Pam");
            Server.System<SharedHandsSystem>().TryPickupAnyHand(payer, id);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, payer);

            invoice = SEntMan.SpawnEntity("Invoice", map.GridCoords);
            var comp = SEntMan.GetComponent<InvoiceComponent>(invoice);
            comp.Cost = 40;
            comp.TargetPerson = "Payee Pete";
        });
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(invoice, new InvoicePayMessage(0) { Actor = payer });

            Assert.That(SEntMan.GetComponent<InvoiceComponent>(invoice).Paid);
            Assert.That(bank.GetBalance(payer), Is.EqualTo(BankSystem.StartingBalance - 40));
            Assert.That(bank.TryGetBalance("Payee Pete", out var payee) ? payee : 0, Is.EqualTo(40),
                "Characters get paid even when nobody's playing them");
        });
    }
}
