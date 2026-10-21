using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.GridControl;

/// <summary>
/// An anchor key: parks the grid it's on in bluespace (saving it and taking it out of the world), and brings it back
/// near where it was parked. Ported from SS14-Persistence.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedBluespaceParkingSystem))]
public sealed partial class BSPAnchorKeyComponent : Component
{
    public const string PrivilegedIdCardSlotId = "BSPAnchorKey-privilegedId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    [DataField]
    public TimeSpan? RoutineStartTime;

    [DataField]
    public BSPState State = BSPState.Idle;

    /// <summary>
    /// The grid being parked.
    /// </summary>
    [ViewVariables]
    public EntityUid? CurrentTarget;

    #region Parked grid

    /// <summary>
    /// The parked grid's save file.
    /// </summary>
    [DataField]
    public string? SavedFilename;

    [DataField]
    public DateTime? SavedTimestamp;

    /// <summary>
    /// The faction (by id) that owned the grid when it was parked.
    /// </summary>
    [DataField]
    public int? SavedOwnerFaction;

    /// <summary>
    /// The character that personally owned the grid when it was parked.
    /// </summary>
    [DataField]
    public string? SavedOwnerPersonal;

    /// <summary>
    /// Whether to drop the grid's ownership when it's unparked.
    /// </summary>
    [DataField]
    public bool SavedClearOwnership;

    [DataField]
    public Vector2? SavedParkedPosition;

    [DataField]
    public Box2? SavedParkedBounds;

    [DataField]
    public Angle? SavedParkedRotation;

    [DataField]
    public int? SavedTileCount;

    [DataField]
    public string? SavedGridName;

    #endregion
}

/// <summary>
/// Marks a grid that an anchor key is parking.
/// </summary>
[RegisterComponent]
[Access(typeof(SharedBluespaceParkingSystem))]
public sealed partial class BSPParkingTargetComponent : Component
{
    [ViewVariables]
    public EntityUid Source;

    [ViewVariables]
    public bool RampingUp;

    [ViewVariables]
    public EntityUid? StartupStream;
}

/// <summary>
/// Marks an anchor key that's unparking its grid.
/// </summary>
[RegisterComponent]
[Access(typeof(SharedBluespaceParkingSystem))]
public sealed partial class BSPAnchorKeyUnparkingComponent : Component
{
    [ViewVariables]
    public Vector2 Origin;

    [ViewVariables]
    public Angle Rotation;
}

[Serializable, NetSerializable]
public enum BSPState : byte
{
    /// <summary>
    /// Holding nothing, ready to park the grid it's on.
    /// </summary>
    Idle,

    Parking,

    /// <summary>
    /// Holding a parked grid, which can be brought back near where it was parked.
    /// </summary>
    Parked,

    Unparking,
}

[Serializable, NetSerializable]
public sealed class BSPAnchorKeyBoundUserInterfaceState : BoundUserInterfaceState
{
    public BSPState State;
    public bool IsAuth;
    public string? OwnerName;
    public string? GridName;
    public string? IdName;

    public int GridTileCount;
    public int GridOwnerTotalTiles;
    public int TileLimit;

    public string? ErrorMessage;

    public TimeSpan? RoutineStartTime;
    public int ParkDelay;
    public int UnparkDelay;
    public Vector2? ParkWorldPosition;
    public float UnparkMaxDistance;
    public bool ClearOwnership;

    public bool CanAct => GridName != null && IsAuth && ErrorMessage == null;
}

[Serializable, NetSerializable]
public enum BSPAnchorKeyUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum BSPAnchorKeyVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum BSPAnchorKeyVisualLayers : byte
{
    Base,
}

[Serializable, NetSerializable]
public enum BSPAnchorKeyVisualState : byte
{
    NoIdUnstored,
    NoIdStored,
    IdUnstored,
    IdStored,
}

[Serializable, NetSerializable]
public sealed class BSPAnchorKeyStartPark : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class BSPAnchorKeyCancel : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class BSPAnchorKeyStartUnpark : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class BSPAnchorKeyToggleClearOwnership : BoundUserInterfaceMessage;
