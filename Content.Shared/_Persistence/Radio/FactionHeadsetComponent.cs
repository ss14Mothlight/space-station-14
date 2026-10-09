using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Radio;

/// <summary>
/// Which faction a headset talks and listens on. Talking on a channel the faction has enabled goes out over that
/// faction's radio instead of the encryption keys, to everyone tuned in who has access to the channel there.
/// Ported from SS14-Persistence, where this replaced encryption keys entirely.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionHeadsetComponent : Component
{
    /// <summary>
    /// The faction (by id) this headset transmits for, or 0 to use the encryption keys.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int TransmitTo;

    /// <summary>
    /// The faction (by id) this headset listens to, or 0 for every faction the wearer belongs to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ReceiveFrom;
}

[Serializable, NetSerializable]
public enum FactionHeadsetUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class FactionHeadsetBoundUserInterfaceState(Dictionary<int, string> factions, int transmitTo, int receiveFrom)
    : BoundUserInterfaceState
{
    public readonly Dictionary<int, string> Factions = factions;
    public readonly int TransmitTo = transmitTo;
    public readonly int ReceiveFrom = receiveFrom;
}

[Serializable, NetSerializable]
public sealed class FactionHeadsetTransmitSelect(int faction) : BoundUserInterfaceMessage
{
    public readonly int Faction = faction;
}

[Serializable, NetSerializable]
public sealed class FactionHeadsetReceiveSelect(int faction) : BoundUserInterfaceMessage
{
    public readonly int Faction = faction;
}
