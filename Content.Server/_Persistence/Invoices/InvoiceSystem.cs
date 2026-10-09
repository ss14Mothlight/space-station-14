using Content.Server._Mothlight.Economy;
using Content.Server._Persistence.Factions;
using Content.Server.Access.Systems;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared._Persistence.Invoices;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Persistence.Invoices;

/// <summary>
/// Invoices and payslips, ported from SS14-Persistence. People pay with their credits, factions with their treasury
/// (within the payer's spending limit), and the faction owning the grid an invoice was printed on takes its sales tax.
/// </summary>
public sealed partial class InvoiceSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = null!;
    [Dependency] private AppearanceSystem _appearance = null!;
    [Dependency] private SharedAudioSystem _audio = null!;
    [Dependency] private CharacterCreditsSystem _credits = null!;
    [Dependency] private FactionSystem _faction = null!;
    [Dependency] private HandsSystem _hands = null!;
    [Dependency] private IdCardSystem _idCard = null!;
    [Dependency] private MetaDataSystem _metaData = null!;
    [Dependency] private PopupSystem _popup = null!;
    [Dependency] private UserInterfaceSystem _ui = null!;

    public static readonly EntProtoId InvoicePrototype = "Invoice";
    public static readonly EntProtoId PayslipPrototype = "Payslip";

    public const int MaxCost = 1_000_000;
    public const int MaxTextLength = 300;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InvoiceComponent, ComponentStartup>(OnInvoiceStartup);
        SubscribeLocalEvent<InvoiceComponent, BoundUIOpenedEvent>(OnInvoiceOpened);
        SubscribeLocalEvent<InvoiceComponent, InvoicePayMessage>(OnPay);

        SubscribeLocalEvent<InvoicePrinterConsoleComponent, EntInsertedIntoContainerMessage>((uid, comp, _) => UpdatePrinter((uid, comp)));
        SubscribeLocalEvent<InvoicePrinterConsoleComponent, EntRemovedFromContainerMessage>((uid, comp, _) => UpdatePrinter((uid, comp)));
        SubscribeLocalEvent<InvoicePrinterConsoleComponent, BoundUIOpenedEvent>((uid, comp, _) => UpdatePrinter((uid, comp)));
        SubscribeLocalEvent<InvoicePrinterConsoleComponent, InvoicePrinterSetModeMessage>(OnSetMode);
        SubscribeLocalEvent<InvoicePrinterConsoleComponent, InvoicePrinterSelectStationMessage>(OnSelectStation);
        SubscribeLocalEvent<InvoicePrinterConsoleComponent, InvoicePrinterPrintMessage>(OnPrint);
    }

    private string? GetIdName(EntityUid user)
    {
        return _idCard.TryFindIdCard(user, out var id) ? id.Comp.FullName : null;
    }

    #region Printer

    private void OnSetMode(Entity<InvoicePrinterConsoleComponent> ent, ref InvoicePrinterSetModeMessage args)
    {
        ent.Comp.Mode = args.Mode;
        UpdatePrinter(ent);
    }

    private void OnSelectStation(Entity<InvoicePrinterConsoleComponent> ent, ref InvoicePrinterSelectStationMessage args)
    {
        ent.Comp.SelectedStation = args.Station;
        UpdatePrinter(ent);
    }

    private void OnPrint(Entity<InvoicePrinterConsoleComponent> ent, ref InvoicePrinterPrintMessage args)
    {
        if (args.Cost <= 0
            || args.Cost > MaxCost
            || ent.Comp.PrivilegedIdSlot.Item is not { } id
            || _faction.GetIdName(id) is not { } holder)
        {
            return;
        }

        var title = Truncate(args.Title);
        var reason = Truncate(args.Reason);
        var taxOwner = _faction.GetOwningFaction(ent) is { } taxing ? _faction.GetStationID(taxing) : 0;

        string? targetPerson = null;
        int? targetStation = null;
        var payslip = false;

        switch (ent.Comp.Mode)
        {
            case InvoicePrinterMode.PersonalInvoice:
                targetPerson = holder;
                break;

            case InvoicePrinterMode.FactionInvoice:
                if (_faction.GetStationByID(ent.Comp.SelectedStation) is not { } payee
                    || !_faction.HasRecord(holder, payee))
                {
                    return;
                }

                targetStation = ent.Comp.SelectedStation;
                break;

            case InvoicePrinterMode.FactionPayslip:
                // The faction pays up front, so it has to be someone allowed to spend that much.
                if (_faction.GetStationByID(ent.Comp.SelectedStation) is not { } payer)
                    return;

                if (!_faction.CanSpend(holder, payer, args.Cost))
                {
                    _popup.PopupCursor(Loc.GetString("faction-spending-limit-reached"), args.Actor);
                    return;
                }

                if (!_faction.TryAdjustFactionBalance(payer, -args.Cost))
                {
                    _popup.PopupCursor(Loc.GetString("invoice-insufficient-funds"), args.Actor);
                    return;
                }

                _faction.TrackSpending(holder, payer, args.Cost);
                targetStation = ent.Comp.SelectedStation;
                payslip = true;
                break;
        }

        var paper = Spawn(payslip ? PayslipPrototype : InvoicePrototype, Transform(args.Actor).Coordinates);
        var invoice = EnsureComp<InvoiceComponent>(paper);
        invoice.Cost = args.Cost;
        invoice.Reason = reason;
        invoice.TargetPerson = targetPerson;
        invoice.TargetStation = targetStation;
        invoice.TaxOwner = payslip ? 0 : taxOwner;
        invoice.Payslip = payslip;
        Dirty(paper, invoice);

        var name = Loc.GetString(payslip ? "invoice-payslip-name" : "invoice-name",
            ("cost", args.Cost),
            ("title", title));
        _metaData.SetEntityName(paper, name);
        _hands.TryPickupAnyHand(args.Actor, paper);
        _audio.PlayPvs(ent.Comp.PrintSound, ent);

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} ({holder}) printed {ToPrettyString(paper)} for {args.Cost}");
        UpdatePrinter(ent);
    }

    private static string Truncate(string text)
    {
        text = text.Trim();
        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    private void UpdatePrinter(Entity<InvoicePrinterConsoleComponent> ent)
    {
        var state = new InvoicePrinterConsoleBoundUserInterfaceState
        {
            Mode = ent.Comp.Mode,
            SelectedStation = ent.Comp.SelectedStation,
        };

        if (ent.Comp.PrivilegedIdSlot.Item is { } id)
        {
            state.IdName = Name(id);
            state.HolderName = _faction.GetIdName(id);
        }

        if (state.HolderName != null)
        {
            foreach (var station in _faction.GetStationsAvailableTo(state.HolderName))
            {
                state.Factions[_faction.GetStationID(station)] = Name(station);
            }
        }

        if (_faction.GetOwningFaction(ent) is { } taxing)
        {
            state.TaxingFaction = Name(taxing);
            state.TaxRate = Comp<FactionDataComponent>(taxing).SalesTax;
        }

        _ui.SetUiState(ent.Owner, InvoicePrinterConsoleUiKey.Key, state);
    }

    #endregion

    #region Invoices

    private void OnInvoiceStartup(Entity<InvoiceComponent> ent, ref ComponentStartup args)
    {
        // Appearance data isn't saved, so loaded invoices need telling whether they're paid.
        _appearance.SetData(ent, InvoiceVisuals.Paid, ent.Comp.Paid);
    }

    private void OnInvoiceOpened(Entity<InvoiceComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateInvoice(ent, args.Actor);
    }

    private int GetTaxRate(InvoiceComponent invoice)
    {
        return _faction.GetStationByID(invoice.TaxOwner) is { } taxing
            ? Comp<FactionDataComponent>(taxing).SalesTax
            : 0;
    }

    private void OnPay(Entity<InvoiceComponent> ent, ref InvoicePayMessage args)
    {
        if (ent.Comp.Paid)
            return;

        var user = args.Actor;
        var userName = GetIdName(user) ?? Name(user);
        var cost = ent.Comp.Cost;
        EntityUid? faction = args.Station == 0 ? null : _faction.GetStationByID(args.Station);
        if (args.Station != 0 && faction == null)
            return;

        string paidBy;
        if (ent.Comp.Payslip)
        {
            // Redeeming: the faction already paid, so this just hands the money over.
            if (faction is { } into)
            {
                if (!_faction.HasRecord(userName, into))
                    return;

                _faction.TryAdjustFactionBalance(into, cost);
                paidBy = $"{Name(into)} ({userName})";
            }
            else
            {
                if (!_credits.TryChangeBalance(user, cost))
                    return;

                paidBy = userName;
            }
        }
        else
        {
            if (faction is { } from)
            {
                if (!_faction.CanSpend(userName, from, cost))
                {
                    Fail(ent, user, "faction-spending-limit-reached");
                    return;
                }

                if (!_faction.TryAdjustFactionBalance(from, -cost))
                {
                    Fail(ent, user, "invoice-insufficient-funds");
                    return;
                }

                _faction.TrackSpending(userName, from, cost);
                paidBy = $"{Name(from)} ({userName})";
            }
            else
            {
                if (!_credits.TryChangeBalance(user, -cost))
                {
                    Fail(ent, user, "invoice-insufficient-funds");
                    return;
                }

                paidBy = userName;
            }

            // The tax comes out of what the payee gets, not on top of it.
            var tax = (int) Math.Round(cost * GetTaxRate(ent.Comp) / 100f);
            if (tax > 0 && _faction.GetStationByID(ent.Comp.TaxOwner) is { } taxing)
            {
                _faction.TryAdjustFactionBalance(taxing, tax);
                cost -= tax;
            }

            if (ent.Comp.TargetStation is { } targetId && _faction.GetStationByID(targetId) is { } target)
                _faction.TryAdjustFactionBalance(target, cost);
            else if (ent.Comp.TargetPerson is { } person)
                _credits.Deposit(person, cost);
        }

        ent.Comp.Paid = true;
        ent.Comp.PaidBy = paidBy;
        ent.Comp.PaidTime = DateTime.Now;
        Dirty(ent);
        _appearance.SetData(ent, InvoiceVisuals.Paid, true);
        _audio.PlayEntity(ent.Comp.PaySuccessSound, user, ent);

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(user):player} {(ent.Comp.Payslip ? "redeemed" : "paid")} {ToPrettyString(ent)} ({ent.Comp.Cost}) as {paidBy}");
        UpdateInvoice(ent, user);
    }

    private void Fail(Entity<InvoiceComponent> ent, EntityUid user, string message)
    {
        _popup.PopupCursor(Loc.GetString(message), user);
        _audio.PlayEntity(ent.Comp.ErrorSound, user, ent);
    }

    private void UpdateInvoice(Entity<InvoiceComponent> ent, EntityUid user)
    {
        var userName = GetIdName(user) ?? Name(user);
        var state = new InvoiceBoundUserInterfaceState
        {
            Cost = ent.Comp.Cost,
            Reason = ent.Comp.Reason,
            Payslip = ent.Comp.Payslip,
            Paid = ent.Comp.Paid,
            PaidBy = ent.Comp.PaidBy,
            PaidTime = ent.Comp.PaidTime,
            TaxRate = ent.Comp.Payslip ? 0 : GetTaxRate(ent.Comp),
            Credits = _credits.GetBalance(user),
        };

        if (ent.Comp.TargetStation is { } targetId && _faction.GetStationByID(targetId) is { } target)
            state.PayTo = Name(target);
        else if (ent.Comp.TargetPerson is { } person)
            state.PayTo = person;

        foreach (var station in _faction.GetStationsAvailableTo(userName))
        {
            // Payslips go into any faction you're with, invoices come out of ones you can spend for.
            if (ent.Comp.Payslip || _faction.CanSpend(userName, station, ent.Comp.Cost))
                state.Factions[_faction.GetStationID(station)] = Name(station);
        }

        _ui.SetUiState(ent.Owner, InvoiceUiKey.Key, state);
    }

    #endregion
}
