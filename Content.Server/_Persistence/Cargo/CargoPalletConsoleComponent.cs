using Content.Shared._Persistence.Cargo;

// ReSharper disable once CheckNamespace
namespace Content.Server.Cargo.Components;

/// <summary>
/// Who a cargo sale console pays, and how. Ported from SS14-Persistence.
/// </summary>
public sealed partial class CargoPalletConsoleComponent
{
    /// <summary>
    /// How sales pay out.
    /// </summary>
    [DataField]
    public CargoSaleMode Mode = CargoSaleMode.Deposit;

    /// <summary>
    /// The faction (by id) deposits go to, or 0 for the one that owns the console.
    /// </summary>
    [DataField]
    public int SelectedStation;
}
