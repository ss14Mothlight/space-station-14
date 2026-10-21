using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Cargo;

/// <summary>
/// How a cargo sale console pays out. Ported from SS14-Persistence.
/// </summary>
[Serializable, NetSerializable]
public enum CargoSaleMode : byte
{
    /// <summary>
    /// Straight into the selected faction's treasury.
    /// </summary>
    Deposit,

    /// <summary>
    /// Spesos, into the seller's hands.
    /// </summary>
    Cash,

    /// <summary>
    /// A payslip, which whoever redeems it can pay into their bank account or a faction.
    /// </summary>
    Payslip,
}

/// <summary>
/// The payout half of a cargo sale console's UI state.
/// </summary>
[Serializable, NetSerializable]
public sealed class CargoPalletSaleState
{
    public CargoSaleMode Mode;

    /// <summary>
    /// Factions the seller can deposit into, by id.
    /// </summary>
    public Dictionary<int, string> Factions = new();

    /// <summary>
    /// The faction deposits go into, or 0 for the one that owns the console.
    /// </summary>
    public int SelectedStation;

    public string? SelectedName;

    /// <summary>
    /// The faction that owns the console, and takes its export tax from the sale.
    /// </summary>
    public string? TaxingName;

    /// <summary>
    /// The export tax this sale would pay, in percent.
    /// </summary>
    public int TaxRate;
}

[Serializable, NetSerializable]
public sealed class CargoPalletSetModeMessage(CargoSaleMode mode) : BoundUserInterfaceMessage
{
    public readonly CargoSaleMode Mode = mode;
}

[Serializable, NetSerializable]
public sealed class CargoPalletSelectStationMessage(int station) : BoundUserInterfaceMessage
{
    public readonly int Station = station;
}
