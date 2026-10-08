using System.Numerics;
using Robust.Shared.Network;

namespace Content.Server._Mothlight.Persistence;

/// <summary>
/// Marks a mob as a persistent player character, and remembers which save file it belongs to.
/// This is saved along with the mob, so a loaded character still knows who it is.
/// </summary>
[RegisterComponent]
public sealed partial class PersistentCharacterComponent : Component
{
    /// <summary>
    /// The user that owns this character.
    /// </summary>
    [DataField]
    public NetUserId UserId;

    /// <summary>
    /// The name of the character profile this mob was spawned from. Used as the save key, since the
    /// entity name itself can be changed in-round (loadout names, renaming, etc).
    /// </summary>
    [DataField]
    public string ProfileName = string.Empty;

    /// <summary>
    /// Where the character was on the round's main map when it was last saved, if it was on it at all.
    /// Used to put them back where they left off when the world is persistent.
    /// </summary>
    [DataField]
    public Vector2? LastWorldPosition;
}
