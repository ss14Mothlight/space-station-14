using Content.Shared._Persistence.Factions.Components;
using Content.Shared._Persistence.Factions.Prototypes;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Factions.BUI;

[Serializable, NetSerializable]
public enum StationModUiKey : byte
{
    StationMod,
}

[Serializable, NetSerializable]
public sealed class StationModificationInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// False if the console isn't on a faction's grid, in which case nothing else is filled in.
    /// </summary>
    public bool HasFaction = true;

    public string Name = string.Empty;
    public string FactionTag = string.Empty;
    public List<string> Owners = new();
    public List<string> CrewAccesses = new();
    public Dictionary<int, CrewAssignment> CrewAssignments = new();
    public int ImportTax;
    public int ExportTax;
    public int SalesTax;
    public ProtoId<FactionLevelPrototype> Level;
    public int AccountBalance;
    public Dictionary<ProtoId<RadioChannelPrototype>, FactionRadioData> RadioData = new();
    public bool JobNetEnabled;
    public bool TradeStationClaimed;
}

[Serializable, NetSerializable]
public enum FactionTaxType : byte
{
    Import,
    Export,
    Sales,
}

[Serializable, NetSerializable]
public enum AssignmentValue : byte
{
    Wage,
    CommandLevel,
    SpendingLimit,
}

[Serializable, NetSerializable]
public enum AssignmentFlag : byte
{
    CanAssign,
    CanClaim,
    CanEditRecords,
}

[Serializable, NetSerializable]
public sealed class StationModificationChangeName(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

[Serializable, NetSerializable]
public sealed class StationModificationChangeFactionTag(string tag) : BoundUserInterfaceMessage
{
    public readonly string Tag = tag;
}

[Serializable, NetSerializable]
public sealed class StationModificationAddOwner(string owner) : BoundUserInterfaceMessage
{
    public readonly string Owner = owner;
}

[Serializable, NetSerializable]
public sealed class StationModificationRemoveOwner(string owner) : BoundUserInterfaceMessage
{
    public readonly string Owner = owner;
}

[Serializable, NetSerializable]
public sealed class StationModificationAddAccess(string access) : BoundUserInterfaceMessage
{
    public readonly string Access = access;
}

[Serializable, NetSerializable]
public sealed class StationModificationRemoveAccess(string access) : BoundUserInterfaceMessage
{
    public readonly string Access = access;
}

/// <summary>
/// Adds every normal access level as a faction access, so assignments can grant them.
/// </summary>
[Serializable, NetSerializable]
public sealed class StationModificationDefaultAccess : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class StationModificationSetJobNet(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class StationModificationChangeTax(FactionTaxType type, int value) : BoundUserInterfaceMessage
{
    public readonly FactionTaxType Type = type;
    public readonly int Value = value;
}

[Serializable, NetSerializable]
public sealed class StationModificationCreateAssignment(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

[Serializable, NetSerializable]
public sealed class StationModificationDeleteAssignment(int assignment) : BoundUserInterfaceMessage
{
    public readonly int Assignment = assignment;
}

[Serializable, NetSerializable]
public sealed class StationModificationChangeAssignmentName(int assignment, string name) : BoundUserInterfaceMessage
{
    public readonly int Assignment = assignment;
    public readonly string Name = name;
}

[Serializable, NetSerializable]
public sealed class StationModificationChangeAssignmentValue(int assignment, AssignmentValue type, int value)
    : BoundUserInterfaceMessage
{
    public readonly int Assignment = assignment;
    public readonly AssignmentValue Type = type;
    public readonly int Value = value;
}

[Serializable, NetSerializable]
public sealed class StationModificationToggleAssignmentFlag(int assignment, AssignmentFlag flag)
    : BoundUserInterfaceMessage
{
    public readonly int Assignment = assignment;
    public readonly AssignmentFlag Flag = flag;
}

[Serializable, NetSerializable]
public sealed class StationModificationSetAssignmentAccess(int assignment, string access, bool enabled)
    : BoundUserInterfaceMessage
{
    public readonly int Assignment = assignment;
    public readonly string Access = access;
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class StationModificationSetChannelEnabled(ProtoId<RadioChannelPrototype> channel, bool enabled)
    : BoundUserInterfaceMessage
{
    public readonly ProtoId<RadioChannelPrototype> Channel = channel;
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class StationModificationSetChannelAccess(ProtoId<RadioChannelPrototype> channel, string access, bool enabled)
    : BoundUserInterfaceMessage
{
    public readonly ProtoId<RadioChannelPrototype> Channel = channel;
    public readonly string Access = access;
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class StationModificationPurchaseUpgrade : BoundUserInterfaceMessage;
