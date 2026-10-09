using Robust.Shared.GameStates;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// A console the owners of the faction it sits on use to run it: owners, name and tag, accesses, assignments,
/// radio channels, taxes and faction level.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StationModificationConsoleComponent : Component;
