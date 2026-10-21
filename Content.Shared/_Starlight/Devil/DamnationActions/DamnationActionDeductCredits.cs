using Content.Shared._Persistence.Bank; // Mothlight

namespace Content.Shared._Starlight.Devil.DamnationActions;

public sealed partial class DamnationActionDeductCredits : DamnationAction
{
    [DataField]
    int Amount = 5000;

    // Mothlight begin - takes spesos out of the victim's bank account
    private SharedBankSystem _bank = default!;

    public override bool Action(Entity<DamnedComponent> victim)
    {
        return _bank.TryBankWithdraw(victim.Owner, Amount);
    }

    public override void ResolveIoC()
    {
        base.ResolveIoC();
        _bank = _entityManager.System<SharedBankSystem>();
    }
    // Mothlight end
}
