using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Factions.BUI;

/// <summary>
/// The faction side of an access reader, shown in the door electronics and access overrider UIs.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionAccessReaderState
{
    /// <summary>
    /// The faction owning the reader's grid, null if there is none.
    /// </summary>
    public string? FactionName;

    /// <summary>
    /// Every custom access the faction has defined.
    /// </summary>
    public List<string> FactionAccesses = new();

    /// <summary>
    /// The faction accesses this reader requires (any one of them).
    /// </summary>
    public List<string> RequiredAccesses = new();

    /// <summary>
    /// The faction accesses the person configuring the reader may add or remove.
    /// </summary>
    public List<string> EditableAccesses = new();

    public bool PersonalAccessMode;

    public List<string> PersonalAccessNames = new();
}

[Serializable, NetSerializable]
public sealed class FactionAccessReaderToggleMessage(string access) : BoundUserInterfaceMessage
{
    public readonly string Access = access;
}

[Serializable, NetSerializable]
public sealed class FactionAccessReaderPersonalModeMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class FactionAccessReaderPersonalAddMessage(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

[Serializable, NetSerializable]
public sealed class FactionAccessReaderPersonalRemoveMessage(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}
