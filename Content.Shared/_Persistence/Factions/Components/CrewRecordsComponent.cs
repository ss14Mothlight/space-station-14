using System.Diagnostics.CodeAnalysis;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// Everyone a faction has on file, keyed by character name, and the assignment each of them holds.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrewRecordsComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<string, CrewRecord> CrewRecords = new();

    public bool TryGetRecord(string name, [NotNullWhen(true)] out CrewRecord? record)
    {
        return CrewRecords.TryGetValue(name, out record);
    }
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class CrewRecord
{
    [DataField("_name")]
    public string Name = "Unnamed Crew Record";

    [DataField("_assignmentid")]
    public int AssignmentID;

    /// <summary>
    /// How much of the faction's money this person has spent since their spending was last reset.
    /// </summary>
    [DataField("_spent")]
    public int Spent;

    // Mothlight: the record texts aren't networked with the component, every client would get every faction's
    // criminal and medical records otherwise. The ID card console sends them to whoever has it open.
    [DataField("_generalRecord"), NonSerialized]
    public string GeneralRecord = string.Empty;

    [DataField("_criminalRecord"), NonSerialized]
    public string CriminalRecord = string.Empty;

    [DataField("_medicalRecord"), NonSerialized]
    public string MedicalRecord = string.Empty;

    [DataField]
    public DateTime LastPaid = DateTime.MinValue;

    public CrewRecord()
    {
    }

    public CrewRecord(string name)
    {
        Name = name;
    }
}
