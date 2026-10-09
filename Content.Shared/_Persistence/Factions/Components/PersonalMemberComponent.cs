using Robust.Shared.GameStates;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// Marks a grid as claimed by a single character instead of a faction.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PersonalMemberComponent : Component
{
    [DataField, AutoNetworkedField]
    public string OwnerName = string.Empty;
}
