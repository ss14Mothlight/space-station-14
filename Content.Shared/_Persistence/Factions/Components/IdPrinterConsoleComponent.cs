using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// Prints a replacement ID for whoever uses it. Their older printed IDs stop working, by being deleted.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IdPrinterConsoleComponent : Component
{
    [DataField]
    public EntProtoId IdCard = "AssistantIDCard";
}

[Serializable, NetSerializable]
public enum IdPrinterConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class IdPrinterPrintMessage : BoundUserInterfaceMessage;
