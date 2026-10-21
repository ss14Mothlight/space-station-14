using System.Globalization;
using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Persistence.Bank;

/// <summary>
/// Characters' bank accounts, in spesos. Ported from SS14-Persistence (originally New Frontiers).
/// The server keeps the real ledger; the client only knows its own character's balance, which is enough to show it
/// and to predict whether something is affordable.
/// </summary>
public abstract partial class SharedBankSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BankATMComponent, ComponentInit>((uid, comp, _) =>
            _itemSlots.AddItemSlot(uid, BankATMComponent.CashSlotId, comp.CashSlot));
        SubscribeLocalEvent<BankATMComponent, ComponentRemove>((uid, comp, _) =>
            _itemSlots.RemoveItemSlot(uid, comp.CashSlot));
    }

    /// <summary>
    /// Gets the bank balance of the character a mob is.
    /// </summary>
    /// <returns>False if they don't have a bank account.</returns>
    public virtual bool TryGetBalance(EntityUid mob, out int balance)
    {
        balance = 0;
        if (!TryComp<BankAccountComponent>(mob, out var account))
            return false;

        balance = account.Balance;
        return true;
    }

    /// <summary>
    /// Takes money out of the bank account of the character a mob is.
    /// </summary>
    /// <returns>False, without taking anything, if they can't afford it or don't have an account.</returns>
    public virtual bool TryBankWithdraw(EntityUid mob, int amount)
    {
        return amount > 0 && TryGetBalance(mob, out var balance) && balance >= amount;
    }

    protected void SetMirroredBalance(Entity<BankAccountComponent> mob, int balance)
    {
        if (mob.Comp.Balance == balance)
            return;

        mob.Comp.Balance = balance;
        Dirty(mob);
    }

    /// <summary>
    /// Formats an amount of spesos, e.g. $1,234.
    /// </summary>
    public static string ToSpesoString(int amount)
    {
        var format = (NumberFormatInfo) CultureInfo.CurrentCulture.NumberFormat.Clone();
        format.CurrencySymbol = "$";
        format.CurrencyPositivePattern = 0; // $N
        format.CurrencyNegativePattern = 1; // -$N
        return string.Format(format, "{0:C0}", amount);
    }
}
