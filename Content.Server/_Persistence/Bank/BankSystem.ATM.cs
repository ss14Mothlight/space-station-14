using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._Persistence.Bank;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Persistence.Bank;

/// <summary>
/// ATMs: put spesos in the slot to deposit them, or withdraw them as cash. Ported from SS14-Persistence.
/// </summary>
public sealed partial class BankSystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = null!;
    [Dependency] private SharedAudioSystem _audio = null!;
    [Dependency] private SharedContainerSystem _container = null!;
    [Dependency] private HandsSystem _hands = null!;
    [Dependency] private PopupSystem _popup = null!;
    [Dependency] private StackSystem _stack = null!;
    [Dependency] private SharedTransformSystem _transform = null!;
    [Dependency] private UserInterfaceSystem _ui = null!;

    private void InitializeATM()
    {
        SubscribeLocalEvent<BankATMComponent, BoundUIOpenedEvent>((uid, comp, args) => UpdateATM((uid, comp), args.Actor));
        SubscribeLocalEvent<BankATMComponent, BankWithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<BankATMComponent, BankDepositMessage>(OnDeposit);
        SubscribeLocalEvent<BankATMComponent, EntInsertedIntoContainerMessage>((uid, comp, _) => UpdateATMUsers((uid, comp)));
        SubscribeLocalEvent<BankATMComponent, EntRemovedFromContainerMessage>((uid, comp, _) => UpdateATMUsers((uid, comp)));
    }

    private void OnWithdraw(Entity<BankATMComponent> ent, ref BankWithdrawMessage args)
    {
        var user = args.Actor;
        if (args.Amount <= 0)
            return;

        if (!TryGetBalance(user, out var balance))
        {
            Deny(ent, user, "bank-atm-menu-no-bank");
            return;
        }

        if (balance < args.Amount)
        {
            Deny(ent, user, "bank-insufficient-funds");
            return;
        }

        if (!TryBankWithdraw(user, args.Amount))
        {
            Deny(ent, user, "bank-atm-menu-transaction-denied");
            return;
        }

        var cash = _stack.SpawnAtPosition(args.Amount, ent.Comp.CashType, Transform(user).Coordinates);
        if (!_hands.TryPickupAnyHand(user, cash))
            _transform.SetLocalRotation(cash, Angle.Zero); // Line it up with the grid instead of the map.

        Confirm(ent, user, "bank-atm-menu-withdraw-successful");
        _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):actor} withdrew {args.Amount} spesos from {ToPrettyString(ent)}");
    }

    private void OnDeposit(Entity<BankATMComponent> ent, ref BankDepositMessage args)
    {
        var user = args.Actor;
        if (ent.Comp.CashSlot.ContainerSlot is not { } slot)
            return;

        var deposit = GetInsertedCash(ent);
        if (deposit < 0)
        {
            Deny(ent, user, "bank-atm-menu-wrong-cash");
            return;
        }

        if (deposit == 0)
            return;

        if (!TryGetBalance(user, out _))
        {
            Deny(ent, user, "bank-atm-menu-no-bank");
            return;
        }

        if (!TryBankDeposit(user, deposit))
        {
            Deny(ent, user, "bank-atm-menu-transaction-denied");
            return;
        }

        _container.CleanContainer(slot);
        Confirm(ent, user, "bank-atm-menu-deposit-successful");
        _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):actor} deposited {deposit} spesos into {ToPrettyString(ent)}");
    }

    /// <summary>
    /// How much cash is in the slot, or -1 if it's the wrong kind of cash (or not cash at all).
    /// </summary>
    private int GetInsertedCash(Entity<BankATMComponent> ent)
    {
        if (ent.Comp.CashSlot.Item is not { } cash)
            return 0;

        if (!TryComp<StackComponent>(cash, out var stack) || stack.StackTypeId != ent.Comp.CashType)
            return -1;

        return stack.Count;
    }

    private void Deny(Entity<BankATMComponent> ent, EntityUid user, string message)
    {
        _popup.PopupEntity(Loc.GetString(message), user, user);
        _audio.PlayPvs(ent.Comp.ErrorSound, ent);
        UpdateATM(ent, user);
    }

    private void Confirm(Entity<BankATMComponent> ent, EntityUid user, string message)
    {
        _popup.PopupEntity(Loc.GetString(message), user, user);
        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        UpdateATM(ent, user);
    }

    private void UpdateATMUsers(Entity<BankATMComponent> ent)
    {
        foreach (var user in _ui.GetActors(ent.Owner, BankATMMenuUiKey.ATM))
        {
            UpdateATM(ent, user);
        }
    }

    private void UpdateATM(Entity<BankATMComponent> ent, EntityUid user)
    {
        var hasAccount = TryGetBalance(user, out var balance);
        _ui.SetUiState(ent.Owner, BankATMMenuUiKey.ATM, new BankATMMenuInterfaceState(balance, hasAccount, GetInsertedCash(ent)));
    }
}
