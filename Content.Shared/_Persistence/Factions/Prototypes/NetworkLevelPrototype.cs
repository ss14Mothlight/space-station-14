using Robust.Shared.Prototypes;

namespace Content.Shared._Persistence.Factions.Prototypes;

/// <summary>
/// A character's personal JobNet level, bought in order from JobNet. Each one raises how many tiles they can claim
/// for themselves.
/// </summary>
[Prototype]
public sealed partial class NetworkLevelPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Cost to purchase this level, taken from the character's bank account.
    /// </summary>
    [DataField]
    public int Cost;

    /// <summary>
    /// How many tiles this character is allowed to claim as personal grids.
    /// </summary>
    [DataField]
    public int TileLimit;

    /// <summary>
    /// The level that can be bought after this one, if any.
    /// </summary>
    [DataField]
    public ProtoId<NetworkLevelPrototype>? Next;
}
