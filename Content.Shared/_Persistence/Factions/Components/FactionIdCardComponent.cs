using Robust.Shared.GameStates;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// An ID card tied to its holder's faction employment. While its holder is clocked in to a faction the card shows
/// their assignment there as its job title ("[TAG] Assignment"), and "Off Duty" otherwise.
/// Cards without this keep whatever job title the ID card console gave them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionIdCardComponent : Component
{
    /// <summary>
    /// <see cref="FactionDataComponent.UID"/> of the faction the holder is working for, or 0 for none.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int FactionId;

    /// <summary>
    /// When this card was printed. Printing a new ID for someone deletes their older printed cards.
    /// </summary>
    [DataField]
    public DateTime? CreatedTime;

    /// <summary>
    /// Set when someone writes a job title onto the card at an ID card console. The faction title stops overwriting
    /// it until the holder clocks in or out again.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ManualTitle;
}
