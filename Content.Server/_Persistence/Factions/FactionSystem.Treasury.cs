using Content.Server.Cargo.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Persistence.Factions;

public sealed partial class FactionSystem
{
    [Dependency] private CargoSystem _cargo = null!;

    /// <summary>
    /// The station bank account a faction's money lives in.
    /// </summary>
    public static readonly ProtoId<CargoAccountPrototype> TreasuryAccount = "Cargo";

    public int GetFactionBalance(EntityUid station)
    {
        return TryComp<StationBankAccountComponent>(station, out var bank)
            ? _cargo.GetBalanceFromAccount((station, bank), TreasuryAccount)
            : 0;
    }

    /// <summary>
    /// Adds money to (or, if negative, takes money from) the faction's treasury.
    /// Fails without changing anything if the faction can't afford it.
    /// </summary>
    public bool TryAdjustFactionBalance(EntityUid station, int amount)
    {
        // Station banks always have the cargo account, see StationBankAccountComponent.
        if (!TryComp<StationBankAccountComponent>(station, out var bank)
            || _cargo.GetBalanceFromAccount((station, bank), TreasuryAccount) + amount < 0)
        {
            return false;
        }

        _cargo.UpdateBankAccount((station, bank), amount, TreasuryAccount);
        return true;
    }
}
