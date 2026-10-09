using Content.Shared._Persistence.Factions.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Factions.BUI;

/// <summary>
/// The faction tabs of the ID card console: the faction's crew records, and the selected person's assignment,
/// spending and records.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionIdCardConsoleState
{
    /// <summary>
    /// The faction owning the console's grid. The faction tabs do nothing without one.
    /// </summary>
    public string? FactionName;

    /// <summary>
    /// Whether the privileged ID's holder owns or is employed by the faction, and so can look at its records.
    /// </summary>
    public bool IsMember;

    public bool IsOwner;

    public CrewAssignment? PrivilegedAssignment;

    public Dictionary<int, CrewAssignment> Assignments = new();

    /// <summary>
    /// The assignments the privileged ID's holder may give the selected person (0 for unassigned).
    /// </summary>
    public HashSet<int> AssignableIds = new();

    public List<FactionCrewListEntry> Crew = new();

    /// <summary>
    /// The person selected for editing. Defaults to whoever the target ID belongs to.
    /// </summary>
    public string? SelectedName;

    /// <summary>
    /// Whether the selected person has a record in the faction yet. Giving them an assignment creates one.
    /// </summary>
    public bool SelectedHasRecord;

    public int SelectedAssignmentId;

    public int SelectedSpent;

    public bool CanResetSpending;

    public bool CanEditRecords;

    public string GeneralRecord = string.Empty;

    public string CriminalRecord = string.Empty;

    public string MedicalRecord = string.Empty;
}

[Serializable, NetSerializable]
public readonly record struct FactionCrewListEntry(string Name, string? Assignment, DateTime LastPaid);

[Serializable, NetSerializable]
public enum FactionRecordType : byte
{
    General,
    Criminal,
    Medical,
}

[Serializable, NetSerializable]
public sealed class FactionIdConsoleSelectMessage(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

[Serializable, NetSerializable]
public sealed class FactionIdConsoleAssignMessage(int assignment) : BoundUserInterfaceMessage
{
    public readonly int Assignment = assignment;
}

[Serializable, NetSerializable]
public sealed class FactionIdConsoleResetSpendingMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class FactionIdConsoleSaveRecordMessage(FactionRecordType type, string content) : BoundUserInterfaceMessage
{
    public readonly FactionRecordType Type = type;
    public readonly string Content = content;
}

[Serializable, NetSerializable]
public sealed class FactionIdConsolePrintRecordMessage(FactionRecordType type) : BoundUserInterfaceMessage
{
    public readonly FactionRecordType Type = type;
}
