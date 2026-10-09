using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.GridControl;

[Serializable, NetSerializable]
public enum GridConfigUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum StationCreatorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum StationTaggerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum GridControlConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class GridConfigBoundUserInterfaceState : BoundUserInterfaceState
{
    public string? IdName;
    public bool IsAuth;
    public bool PersonalMode;
    public bool IsControlled;
    public Dictionary<int, string> PossibleStations = new();
    public int? TargetStation;

    /// <summary>
    /// Who grids would be claimed for: the faction, or the ID holder in personal mode.
    /// </summary>
    public string? TargetName;

    /// <summary>
    /// Who has claimed the grid the tool is on, if anyone.
    /// </summary>
    public string? OwnerName;

    public string? GridName;
    public int GridTileCount;
    public int CurrentTileCount;
    public int TileLimit;
    public string? ErrorMessage;
}

[Serializable, NetSerializable]
public sealed class GridConfigChangeName(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

[Serializable, NetSerializable]
public sealed class GridConfigConnect : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class GridConfigDisconnect : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class GridConfigChangeMode : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class GridConfigTargetSelect(int target) : BoundUserInterfaceMessage
{
    public readonly int Target = target;
}

[Serializable, NetSerializable]
public sealed class StationCreatorBoundUserInterfaceState : BoundUserInterfaceState
{
    public string? IdName;
    public string? RealName;
}

[Serializable, NetSerializable]
public sealed class StationCreatorFinish(string stationName) : BoundUserInterfaceMessage
{
    public readonly string StationName = stationName;
}

[Serializable, NetSerializable]
public sealed class StationTaggerBoundUserInterfaceState : BoundUserInterfaceState
{
    public string? IdName;
    public bool IsAuthorized;
    public string? TargetName;
    public string? TaggedFaction;
    public Dictionary<int, string> PossibleStations = new();
    public int? TargetStation;
}

[Serializable, NetSerializable]
public sealed class StationTaggerLink : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class StationTaggerUnlink : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class StationTaggerTargetSelect(int target) : BoundUserInterfaceMessage
{
    public readonly int Target = target;
}

[Serializable, NetSerializable]
public sealed class GridControlConsoleBoundUserInterfaceState(bool active) : BoundUserInterfaceState
{
    public readonly bool Active = active;
}

[Serializable, NetSerializable]
public sealed class GridControlSetActive(bool active) : BoundUserInterfaceMessage
{
    public readonly bool Active = active;
}

[Serializable, NetSerializable]
public sealed partial class StationTaggerDoAfterEvent : SimpleDoAfterEvent;
