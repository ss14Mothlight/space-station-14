using System.Diagnostics.CodeAnalysis;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// The jobs ("assignments") a faction has defined, what each pays and what it can do.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrewAssignmentsComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<int, CrewAssignment> CrewAssignments = new();

    [DataField, AutoNetworkedField]
    public int NextID = 1;

    public bool TryGetAssignment(int id, [NotNullWhen(true)] out CrewAssignment? assignment)
    {
        assignment = null;
        return id != 0 && CrewAssignments.TryGetValue(id, out assignment);
    }

    public CrewAssignment CreateAssignment(string name, int wage = 0, int clevel = 0)
    {
        var assignment = new CrewAssignment(NextID++, name, wage, clevel);
        CrewAssignments.Add(assignment.ID, assignment);
        return assignment;
    }
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class CrewAssignment
{
    [DataField("_id")]
    public int ID;

    [DataField("_name")]
    public string Name = "Unnamed Crew Assignment";

    /// <summary>
    /// Paid out of the faction's account every JobNet pay period while clocked in.
    /// </summary>
    [DataField("_wage")]
    public int Wage;

    /// <summary>
    /// Command level. Someone can only assign jobs with a lower command level than their own.
    /// </summary>
    [DataField("_clevel")]
    public int Clevel;

    /// <summary>
    /// Accesses this assignment grants on the faction's grids. Either access level prototype ids or the
    /// faction's own custom accesses (see <see cref="CrewAccessesComponent"/>).
    /// </summary>
    [DataField("_accessids")]
    public List<string> AccessIDs = new();

    [DataField("_canAssign")]
    public bool CanAssign;

    [DataField("_canClaim")]
    public bool CanClaim;

    [DataField("_spendingLimit")]
    public int SpendingLimit;

    [DataField("_canEditGeneralRecord")]
    public bool CanEditGeneralRecord;

    public CrewAssignment()
    {
    }

    public CrewAssignment(int id, string name, int wage, int clevel)
    {
        ID = id;
        Name = name;
        Wage = wage;
        Clevel = clevel;
    }
}
