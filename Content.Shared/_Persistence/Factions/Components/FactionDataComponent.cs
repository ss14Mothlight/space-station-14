using System.Text;
using Content.Shared._Persistence.Factions.Prototypes;
using Content.Shared.Radio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._Persistence.Factions.Components;

/// <summary>
/// Makes a station a player-run faction: who owns it, its tag, taxes, level and radio setup.
/// Ported from SS14-Persistence, where these fields lived on StationDataComponent.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedFactionSystem), Other = AccessPermissions.ReadExecute)]
public sealed partial class FactionDataComponent : Component
{
    /// <summary>
    /// Hard cap for any faction tag shown in UI, IDs, and IFF labels.
    /// </summary>
    public const int MaxFactionTagLength = 4;

    /// <summary>
    /// Characters who can use the station modification console, and who pass every faction check.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> Owners = new();

    /// <summary>
    /// Stable id for this faction. ID cards, JobNet and grid claims refer to factions by this instead of by entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int UID;

    [DataField, AutoNetworkedField]
    public int ImportTax;

    [DataField, AutoNetworkedField]
    public int ExportTax;

    [DataField, AutoNetworkedField]
    public int SalesTax;

    /// <summary>
    /// Whether members can clock in to this faction over JobNet.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool JobNetEnabled = true;

    [DataField, AutoNetworkedField]
    public ProtoId<FactionLevelPrototype> Level = "FactionLevel1";

    /// <summary>
    /// Which radio channels this faction's headsets get, and which accesses unlock each of them.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<RadioChannelPrototype>, FactionRadioData> RadioData = new()
    {
        { "Common", new FactionRadioData(true) },
        { "Command", new FactionRadioData() },
        { "Engineering", new FactionRadioData() },
        { "Medical", new FactionRadioData() },
        { "Science", new FactionRadioData() },
        { "Security", new FactionRadioData() },
        { "Service", new FactionRadioData() },
        { "Supply", new FactionRadioData() },
        { "Faction", new FactionRadioData() },
        { "STC", new FactionRadioData() },
    };

    /// <summary>
    /// Optional custom faction tag set from the station modification console.
    /// If null or empty, we fall back to an auto-generated tag.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? FactionTag;

    public bool IsOwner(string owner)
    {
        return Owners.Contains(owner);
    }

    /// <summary>
    /// Returns the tag that should actually be displayed to players.
    /// Prefer the configured value, otherwise derive one from the faction name.
    /// </summary>
    public string GetResolvedFactionTag(string factionName)
    {
        var configured = NormalizeFactionTag(FactionTag);
        if (!string.IsNullOrEmpty(configured))
            return configured;

        return GenerateFactionTag(factionName);
    }

    /// <summary>
    /// Sanitizes player input so the tag is compact and predictable.
    /// We strip whitespace and enforce a 4-character cap.
    /// </summary>
    public static string NormalizeFactionTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return string.Empty;

        var sb = new StringBuilder(MaxFactionTagLength);
        foreach (var ch in tag.Trim())
        {
            if (char.IsWhiteSpace(ch))
                continue;

            sb.Append(ch);
            if (sb.Length >= MaxFactionTagLength)
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a default tag from the first letter of each word in the faction name.
    /// Example: "Wayfarer Dynamics" becomes "WD".
    /// </summary>
    public static string GenerateFactionTag(string factionName)
    {
        if (string.IsNullOrWhiteSpace(factionName))
            return string.Empty;

        var sb = new StringBuilder(MaxFactionTagLength);
        var words = factionName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var word in words)
        {
            sb.Append(word[0]);
            if (sb.Length >= MaxFactionTagLength)
                break;
        }

        return sb.ToString();
    }
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class FactionRadioData
{
    [DataField("_enabled")]
    public bool Enabled;

    [DataField("_access")]
    public List<string> Access = new();

    public FactionRadioData()
    {
    }

    public FactionRadioData(bool enabled)
    {
        Enabled = enabled;
    }
}
