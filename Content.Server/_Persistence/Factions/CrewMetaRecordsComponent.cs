using Content.Shared._Persistence.Factions.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// World-wide faction bookkeeping, kept on the default map so it is saved with the world.
/// Holding on to every faction here also keeps factions that own no grids in the world save, since nothing on the
/// map would reference their station entity otherwise.
/// </summary>
[RegisterComponent]
public sealed partial class CrewMetaRecordsComponent : Component
{
    /// <summary>
    /// Every faction, by <see cref="Content.Shared._Persistence.Factions.Components.FactionDataComponent.UID"/>.
    /// </summary>
    [DataField]
    public Dictionary<int, EntityUid> Stations = new();

    /// <summary>
    /// Per-character data that isn't tied to any one faction, keyed by character name.
    /// </summary>
    [DataField]
    public Dictionary<string, CrewMetaRecord> CrewMetaRecords = new();
}

[DataDefinition]
public sealed partial class CrewMetaRecord
{
    [DataField("_name")]
    public string Name = "Unnamed Crew Meta Record";

    /// <summary>
    /// When this character last had a new ID printed. Older printed IDs are no longer valid.
    /// </summary>
    [DataField]
    public DateTime LatestIDTime;

    [DataField]
    public ProtoId<NetworkLevelPrototype> Level = "NetworkLevel1";

    public CrewMetaRecord()
    {
    }

    public CrewMetaRecord(string name)
    {
        Name = name;
    }
}
