using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;

namespace Content.Shared._Persistence.GridControl;

/// <summary>
/// A handheld tool for claiming the grid it's used on for a faction or for its user, and renaming it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GridConfigComponent : Component
{
    public const string PrivilegedIdCardSlotId = "GridConfig-privilegedId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    /// <summary>
    /// The faction (by id) grids get claimed for, when not in personal mode.
    /// </summary>
    [DataField]
    public int? ConnectedStation;

    /// <summary>
    /// Claims grids for the ID's holder instead of a faction.
    /// </summary>
    [DataField]
    public bool PersonalMode;
}

/// <summary>
/// A one-use device that founds a new faction, owned by whoever's ID is in it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StationCreatorComponent : Component
{
    public const string PrivilegedIdCardSlotId = "StationCreator-privilegedId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();
}

/// <summary>
/// A handheld tool that ties a machine or door to a faction, so that faction's accesses apply to it wherever it is.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StationTaggerComponent : Component
{
    public const string PrivilegedIdCardSlotId = "StationTagger-privilegedId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    [DataField]
    public int? ConnectedStation;

    [DataField]
    public float DoAfter = 5f;

    [ViewVariables]
    public EntityUid? Target;
}

/// <summary>
/// While active, the grid this console is on can't be unclaimed.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GridControlConsoleComponent : Component
{
    [DataField]
    public bool Active = true;
}

/// <summary>
/// Ties an entity to a faction regardless of what grid it's on, see <see cref="StationTaggerComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionTagComponent : Component
{
    [DataField, AutoNetworkedField]
    public int FactionId;
}
