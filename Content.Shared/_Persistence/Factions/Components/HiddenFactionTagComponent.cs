using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// Stops a faction's grid showing its faction tag on radar, set from the grid's IFF console.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HiddenFactionTagComponent : Component;

/// <summary>
/// Sent by the IFF console to show or hide its grid's faction tag on radar.
/// </summary>
[Serializable, NetSerializable]
public sealed class IFFShowFactionTagMessage(bool show) : BoundUserInterfaceMessage
{
    public readonly bool Show = show;
}
