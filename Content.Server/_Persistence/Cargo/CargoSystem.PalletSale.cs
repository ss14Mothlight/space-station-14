using Content.Server._Persistence.Invoices;
using Content.Server.Cargo.Components;
using Content.Server.Hands.Systems;
using Content.Shared._Persistence.Cargo;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Database;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

// ReSharper disable once CheckNamespace
namespace Content.Server.Cargo.Systems;

/// <summary>
/// Lets cargo sale consoles pay a sale out to another faction, as spesos, or as a payslip, minus the export tax of the
/// faction that owns the console. Ported from SS14-Persistence.
/// </summary>
public sealed partial class CargoSystem
{
    [Dependency] private HandsSystem _hands = null!;
    [Dependency] private InvoiceSystem _invoice = null!;

    private static readonly ProtoId<StackPrototype> SpesoStack = "Credit";
    private static readonly SoundSpecifier SaleDenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    private void InitializePalletSale()
    {
        SubscribeLocalEvent<CargoPalletConsoleComponent, CargoPalletSetModeMessage>(OnSetSaleMode);
        SubscribeLocalEvent<CargoPalletConsoleComponent, CargoPalletSelectStationMessage>(OnSelectSaleStation);
    }

    private void OnSetSaleMode(Entity<CargoPalletConsoleComponent> ent, ref CargoPalletSetModeMessage args)
    {
        ent.Comp.Mode = args.Mode;
        UpdatePalletConsoleInterface(ent, args.Actor);
    }

    private void OnSelectSaleStation(Entity<CargoPalletConsoleComponent> ent, ref CargoPalletSelectStationMessage args)
    {
        ent.Comp.SelectedStation = args.Station;
        UpdatePalletConsoleInterface(ent, args.Actor);
    }

    /// <summary>
    /// The station that owns the console, if it's on one with a bank to pay into.
    /// </summary>
    private EntityUid? GetSaleOwner(EntityUid console)
    {
        return _station.GetOwningStation(console) is { } station
               && !TerminatingOrDeleted(station)
               && HasComp<StationBankAccountComponent>(station)
            ? station
            : null;
    }

    /// <summary>
    /// The faction a deposit goes to: the selected one, or the one that owns the console.
    /// </summary>
    private EntityUid? GetSaleTarget(Entity<CargoPalletConsoleComponent> console, EntityUid? owner)
    {
        return console.Comp.SelectedStation == 0 ? owner : _faction.GetStationByID(console.Comp.SelectedStation);
    }

    /// <summary>
    /// The export tax, in percent, that the faction owning the console takes from a sale. Selling to yourself is free.
    /// </summary>
    private int GetExportTax(Entity<CargoPalletConsoleComponent> console, EntityUid? owner)
    {
        if (owner == null)
            return 0;

        if (console.Comp.Mode == CargoSaleMode.Deposit && GetSaleTarget(console, owner) == owner)
            return 0;

        return Math.Clamp(CompOrNull<FactionDataComponent>(owner)?.ExportTax ?? 0, 0, 100);
    }

    private CargoPalletSaleState GetSaleState(Entity<CargoPalletConsoleComponent> console, EntityUid? actor)
    {
        var state = new CargoPalletSaleState
        {
            Mode = console.Comp.Mode,
            SelectedStation = console.Comp.SelectedStation,
        };

        if (actor is { } user && GetSellerName(user) is { } name)
        {
            foreach (var station in _faction.GetStationsAvailableTo(name))
            {
                state.Factions[_faction.GetStationID(station)] = Name(station);
            }
        }

        var owner = GetSaleOwner(console);
        if (owner != null)
        {
            state.TaxingName = Name(owner.Value);
            state.TaxRate = GetExportTax(console, owner);
        }

        if (GetSaleTarget(console, owner) is { } target)
            state.SelectedName = Name(target);

        return state;
    }

    private string? GetSellerName(EntityUid user)
    {
        return _faction.GetIdName(_hands.EnumerateHeld(user)) ?? Name(user);
    }

    /// <summary>
    /// Whether the console's payout has somewhere to go, checked before anything gets sold.
    /// </summary>
    private bool CanPayOutSale(Entity<CargoPalletConsoleComponent> console, EntityUid? owner, out string error)
    {
        error = string.Empty;
        if (console.Comp.Mode != CargoSaleMode.Deposit)
            return true;

        if (GetSaleTarget(console, owner) is { } target && HasComp<StationBankAccountComponent>(target))
            return true;

        error = console.Comp.SelectedStation == 0 ? "cargo-pallet-sale-no-station" : "cargo-pallet-sale-no-faction";
        return false;
    }

    /// <summary>
    /// Pays out a sale, unless it's a plain deposit to the console's own faction, which keeps Starlight's usual
    /// split between the station's accounts.
    /// </summary>
    /// <returns>True if the sale was paid out here.</returns>
    private bool TryPayOutSale(Entity<CargoPalletConsoleComponent> console,
        EntityUid? owner,
        EntityUid seller,
        double total)
    {
        var target = GetSaleTarget(console, owner);
        if (console.Comp.Mode == CargoSaleMode.Deposit && target == owner && owner != null)
            return false;

        var amount = (int) Math.Round(total);
        var tax = (int) Math.Round(amount * GetExportTax(console, owner) / 100f);
        var payout = amount - tax;

        if (tax > 0 && owner != null)
            _faction.TryAdjustFactionBalance(owner.Value, tax);

        switch (console.Comp.Mode)
        {
            case CargoSaleMode.Deposit:
                if (target is { } faction && payout > 0)
                    _faction.TryAdjustFactionBalance(faction, payout);
                break;

            case CargoSaleMode.Cash:
                if (payout <= 0)
                    break;

                var cash = _stack.SpawnAtPosition(payout, SpesoStack, Transform(seller).Coordinates);
                if (!_hands.TryPickupAnyHand(seller, cash))
                    _transformSystem.SetLocalRotation(cash, Angle.Zero); // Line it up with the grid instead of the map.
                break;

            case CargoSaleMode.Payslip:
                if (payout <= 0)
                    break;

                var payer = owner is { } payingStation ? Name(payingStation) : Loc.GetString("cargo-pallet-payslip-payer");
                _invoice.SpawnPayslip(seller,
                    payout,
                    Loc.GetString("cargo-pallet-payslip-title"),
                    Loc.GetString("cargo-pallet-payslip-reason", ("faction", payer)),
                    payer);
                break;
        }

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(seller):player} sold cargo for {amount} at {ToPrettyString(console)}, paid out as {console.Comp.Mode} ({payout}, {tax} tax to {(owner is { } taxer ? ToPrettyString(taxer) : "nobody")})");
        return true;
    }
}
