using Content.IntegrationTests.Fixtures;
using Content.Server._Mothlight.Economy;
using Content.Server._Persistence.Factions;
using Content.Server.Access.Systems;
using Content.Shared._Persistence.Invoices;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Mothlight;

[TestFixture]
[TestOf(typeof(CharacterCreditsSystem))]
public sealed class CharacterCreditsTest : GameTest
{
    /// <summary>
    /// Credits belong to the character being played, not the player, and can be paid to characters who aren't
    /// being played.
    /// </summary>
    [Test]
    public async Task CreditsArePerCharacter()
    {
        var map = await Pair.CreateTestMap();
        var credits = Server.System<CharacterCreditsSystem>();

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
            Assert.That(credits.GetBalance(first), Is.EqualTo(CharacterCreditsSystem.StartingCredits));
            Assert.That(credits.TryChangeBalance(first, 50));
            Assert.That(credits.TryChangeBalance(first, -1000), Is.False, "Can't spend more than you have");

            // Someone not being played gets paid next time they are.
            credits.Deposit("Second Sue", 25);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, second);
        });
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(credits.GetBalance(second), Is.EqualTo(CharacterCreditsSystem.StartingCredits + 25));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, first);
        });
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(credits.GetBalance(first), Is.EqualTo(CharacterCreditsSystem.StartingCredits + 50),
                "Switching characters shouldn't carry credits over");
            Assert.That(SEntMan.GetComponent<CharacterCreditsComponent>(first).Balance,
                Is.EqualTo(CharacterCreditsSystem.StartingCredits + 50));
        });
    }

    /// <summary>
    /// Paying an invoice personally moves credits from the payer to whoever it's paid to.
    /// </summary>
    [Test]
    public async Task PayInvoicePersonally()
    {
        var map = await Pair.CreateTestMap();
        var credits = Server.System<CharacterCreditsSystem>();

        EntityUid payer = default, payee = default, invoice = default;
        await Server.WaitPost(() =>
        {
            // Played once, so they have credits, then left behind.
            payee = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Server.System<MetaDataSystem>().SetEntityName(payee, "Payee Pete");
            Server.PlayerMan.SetAttachedEntity(ServerSession!, payee);

            payer = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Server.System<MetaDataSystem>().SetEntityName(payer, "Payer Pam");
            var id = SEntMan.SpawnEntity("AssistantIDCard", map.GridCoords);
            Server.System<IdCardSystem>().TryChangeFullName(id, "Payer Pam");
            Server.System<SharedHandsSystem>().TryPickupAnyHand(payer, id);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, payer);

            invoice = SEntMan.SpawnEntity(InvoiceSystemPrototypes.Invoice, map.GridCoords);
            var comp = SEntMan.GetComponent<InvoiceComponent>(invoice);
            comp.Cost = 40;
            comp.TargetPerson = "Payee Pete";
        });
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var message = new InvoicePayMessage(0) { Actor = payer };
            SEntMan.EventBus.RaiseLocalEvent(invoice, message);

            var comp = SEntMan.GetComponent<InvoiceComponent>(invoice);
            Assert.That(comp.Paid);
            Assert.That(credits.GetBalance(payer), Is.EqualTo(CharacterCreditsSystem.StartingCredits - 40));
            Assert.That(SEntMan.GetComponent<CharacterCreditsComponent>(payee).Balance,
                Is.EqualTo(CharacterCreditsSystem.StartingCredits + 40), "Characters get paid even when nobody's playing them");
        });
    }
}

internal static class InvoiceSystemPrototypes
{
    public static readonly string Invoice = "Invoice";
}
