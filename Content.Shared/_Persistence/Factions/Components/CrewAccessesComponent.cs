using Robust.Shared.GameStates;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// The accesses a faction has defined for itself. Door electronics on the faction's grids can require these, and
/// assignments grant them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrewAccessesComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<string> CrewAccesses = new();
}
