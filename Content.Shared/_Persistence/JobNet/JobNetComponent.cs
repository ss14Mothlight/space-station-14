using Content.Shared._Persistence.Factions.Prototypes;
using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.JobNet;

/// <summary>
/// The job network implant. Lets its holder clock in to a faction they're employed by, which pays their
/// assignment's wage into their bank account every pay period they stay clocked in.
/// Ported from SS14-Persistence, without its crime and objective networks.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class JobNetComponent : Component
{
    /// <summary>
    /// The faction (by id) its holder is clocked in to, or 0 for none.
    /// </summary>
    [DataField]
    public int WorkingFor;

    /// <summary>
    /// How long they've been working towards their next pay.
    /// </summary>
    [DataField]
    public TimeSpan WorkedTime = TimeSpan.Zero;

    /// <summary>
    /// The faction they last worked for. Clocking in somewhere else starts the pay period over.
    /// </summary>
    [DataField]
    public int LastWorkedFor;

    [DataField]
    public TimeSpan PayPeriod = TimeSpan.FromMinutes(20);

    [DataField]
    public SoundSpecifier PaySuccessSound = new SoundPathSpecifier("/Audio/Effects/kaching.ogg");

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");
}

public sealed partial class OpenJobNetImplantEvent : InstantActionEvent;

[Serializable, NetSerializable]
public enum JobNetUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class JobNetUpdateState : BoundUserInterfaceState
{
    /// <summary>
    /// Factions the holder can clock in to, by id.
    /// </summary>
    public Dictionary<int, string> Stations = new();

    public int SelectedStation;
    public string? AssignmentName;
    public int? Wage;
    public TimeSpan RemainingTime;
    public ProtoId<NetworkLevelPrototype> Level = "NetworkLevel1";
    public int Balance;
    public bool SpendAuth;
    public int Spent;
    public int Spendable;
    public bool IsOwner;
}

/// <summary>
/// Clocks in to the faction with the given id, or out with 0.
/// </summary>
[Serializable, NetSerializable]
public sealed class JobNetSelectMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int ID = id;
}

/// <summary>
/// Buys the next network level.
/// </summary>
[Serializable, NetSerializable]
public sealed class JobNetPurchaseMessage : BoundUserInterfaceMessage;
