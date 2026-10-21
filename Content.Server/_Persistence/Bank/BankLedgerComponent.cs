namespace Content.Server._Persistence.Bank;

/// <summary>
/// Every character's bank balance, in spesos, by character name. Kept on the default map so it's saved with the
/// world, and so characters keep their money whether or not anyone's playing them.
/// </summary>
[RegisterComponent, Access(typeof(BankSystem))]
public sealed partial class BankLedgerComponent : Component
{
    [DataField]
    public Dictionary<string, int> Accounts = new();
}
