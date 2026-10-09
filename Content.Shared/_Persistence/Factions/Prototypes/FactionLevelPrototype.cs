using Robust.Shared.Prototypes;

namespace Content.Shared._Persistence.Factions.Prototypes;

/// <summary>
/// Faction levels are bought in order from the station modification console, each one raising what the faction
/// can claim.
/// </summary>
[Prototype]
public sealed partial class FactionLevelPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Cost to purchase this faction level, taken from the faction's account.
    /// </summary>
    [DataField]
    public int Cost;

    /// <summary>
    /// How many tiles this faction is allowed to claim.
    /// </summary>
    [DataField]
    public int TileLimit;

    /// <summary>
    /// Whether the faction can claim a trade station.
    /// </summary>
    [DataField]
    public bool TradestationClaim;

    /// <summary>
    /// The level that can be bought after this one, if any.
    /// </summary>
    [DataField]
    public ProtoId<FactionLevelPrototype>? Next;
}
