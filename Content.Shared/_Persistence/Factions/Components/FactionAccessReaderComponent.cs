using Robust.Shared.GameStates;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// Faction-specific requirements for an access reader, on top of its normal access levels.
/// The reader lets someone through if their faction assignment grants one of <see cref="AccessNames"/> on the
/// faction that owns the reader's grid, or, in personal mode, if they're on <see cref="PersonalAccessNames"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(FactionAccessSystem))]
public sealed partial class FactionAccessReaderComponent : Component
{
    /// <summary>
    /// The owning faction's custom accesses (see <see cref="CrewAccessesComponent"/>), any one of which lets someone in.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> AccessNames = new();

    /// <summary>
    /// If true, the characters on <see cref="PersonalAccessNames"/> get in.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool PersonalAccessMode;

    [DataField, AutoNetworkedField]
    public List<string> PersonalAccessNames = new();

    public bool HasRequirements => AccessNames.Count > 0 || PersonalAccessMode && PersonalAccessNames.Count > 0;
}
