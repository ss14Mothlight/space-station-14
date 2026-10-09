using Robust.Shared.Utility;

namespace Content.Server._Mothlight.Persistence;

/// <summary>
/// Game rule that makes the round's main map persistent: it's loaded from a save at round start (if one exists),
/// and saved again at round end and on a timer.
/// </summary>
[RegisterComponent, Access(typeof(WorldPersistenceRuleSystem))]
public sealed partial class WorldPersistenceRuleComponent : Component
{
    /// <summary>
    /// Where the world is saved, in the server's user data.
    /// </summary>
    /// <remarks>
    /// A string rather than a <see cref="ResPath"/> data field, which would be validated as a content file that has
    /// to exist.
    /// </remarks>
    [DataField("savePath")]
    public string SavePathString = "/Mothlight/World/world.yml";

    public ResPath SavePath => new(SavePathString);

    /// <summary>
    /// How often to save the world while the round is running. Null disables autosaving.
    /// </summary>
    [DataField]
    public TimeSpan? AutosaveInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether to save the world when the round ends.
    /// </summary>
    [DataField]
    public bool SaveOnRoundEnd = true;

    [ViewVariables]
    public TimeSpan NextAutosave;

    /// <summary>
    /// Whether this round's map was loaded from the save, rather than generated fresh.
    /// </summary>
    [ViewVariables]
    public bool LoadedFromSave;

    /// <summary>
    /// Set when a save exists but couldn't be loaded, so the fresh map this round doesn't overwrite it.
    /// Admins can clear this with VV once they've dealt with the broken save.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool SavingDisabled;
}
