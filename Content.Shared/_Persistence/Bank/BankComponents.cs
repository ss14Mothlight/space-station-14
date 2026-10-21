using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Bank;

/// <summary>
/// A copy of the bank balance of the character this mob is, sent only to whoever is playing them, so the client can
/// show it and predict purchases. The real balance lives in the bank's ledger. Ported from SS14-Persistence.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedBankSystem))]
public sealed partial class BankAccountComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Balance;

    public override bool SendOnlyToOwner => true;
}

/// <summary>
/// An ATM, for withdrawing spesos from your bank account and depositing them back in. Ported from SS14-Persistence.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BankATMComponent : Component
{
    public const string CashSlotId = "bank-ATM-cashSlot";

    /// <summary>
    /// The kind of cash the ATM takes and gives out.
    /// </summary>
    [DataField]
    public ProtoId<StackPrototype> CashType = "Credit";

    [DataField]
    public ItemSlot CashSlot = new();

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}

[Serializable, NetSerializable]
public enum BankATMMenuUiKey : byte
{
    ATM,
}

[Serializable, NetSerializable]
public sealed class BankATMMenuInterfaceState(int balance, bool enabled, int deposit) : BoundUserInterfaceState
{
    /// <summary>
    /// The bank balance of whoever is using the ATM.
    /// </summary>
    public readonly int Balance = balance;

    /// <summary>
    /// Whether the user has a bank account to use.
    /// </summary>
    public readonly bool Enabled = enabled;

    /// <summary>
    /// How much cash is inserted, or negative if what's inserted isn't the right cash.
    /// </summary>
    public readonly int Deposit = deposit;
}

/// <summary>
/// Deposits whatever cash is in the ATM. Doesn't take an amount, so clients can't make up how much they put in.
/// </summary>
[Serializable, NetSerializable]
public sealed class BankDepositMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class BankWithdrawMessage(int amount) : BoundUserInterfaceMessage
{
    public readonly int Amount = amount;
}
