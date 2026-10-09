namespace Content.Server._Persistence.Factions;

/// <summary>
/// Who an ID card console's faction tabs are looking at. Added to consoles as they're used.
/// </summary>
[RegisterComponent, Access(typeof(FactionIdCardConsoleSystem))]
public sealed partial class FactionIdCardConsoleComponent : Component
{
    [ViewVariables]
    public string? SelectedName;

    /// <summary>
    /// Whoever the target ID belonged to last time the UI was updated, to notice a new card going in.
    /// </summary>
    [ViewVariables]
    public string? LastTargetName;
}
