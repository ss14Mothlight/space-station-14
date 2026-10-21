using Content.Server._Mothlight.Persistence;
using Content.Server.GameTicking;
using Content.Shared._Persistence.Bank;
using Content.Shared.Humanoid;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._Persistence.Bank;

/// <summary>
/// Characters' bank accounts, in spesos, ported from SS14-Persistence. Accounts belong to characters (by name), not
/// players, and live in a ledger on the default map. Whoever is playing a character gets a copy of its balance in
/// <see cref="BankAccountComponent"/>.
/// </summary>
public sealed partial class BankSystem : SharedBankSystem
{
    [Dependency] private SharedMapSystem _map = null!;
    [Dependency] private GameTicker _ticker = null!;

    /// <summary>
    /// What a new character starts out with.
    /// </summary>
    public const int StartingBalance = 100;

    public override void Initialize()
    {
        base.Initialize();

        InitializeATM();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        // Only characters have bank accounts. Ghosts, observers, mice and the like don't.
        if (!HasComp<HumanoidAppearanceComponent>(ev.Entity) && !HasComp<PersistentCharacterComponent>(ev.Entity))
            return;

        var name = Name(ev.Entity);
        EnsureAccount(name, StartingBalance);
        var account = EnsureComp<BankAccountComponent>(ev.Entity);
        SetMirroredBalance((ev.Entity, account), GetLedger().Comp.Accounts[name]);
    }

    private Entity<BankLedgerComponent> GetLedger()
    {
        // Kept on the default map so it's saved with the world. Without one (e.g. in tests), it lives in nullspace
        // until there is one.
        EntityUid? defaultMap = _map.TryGetMap(_ticker.DefaultMap, out var map) && !TerminatingOrDeleted(map)
            ? map.Value
            : null;

        var query = EntityQueryEnumerator<BankLedgerComponent>();
        if (query.MoveNext(out var uid, out var existing))
        {
            if (defaultMap == null || uid == defaultMap)
                return (uid, existing);

            // Started out without a map, move it onto the map now there is one.
            var moved = EnsureComp<BankLedgerComponent>(defaultMap.Value);
            foreach (var (name, balance) in existing.Accounts)
            {
                moved.Accounts[name] = moved.Accounts.GetValueOrDefault(name) + balance;
            }

            if (HasComp<MapComponent>(uid))
                RemComp<BankLedgerComponent>(uid);
            else
                Del(uid);

            return (defaultMap.Value, moved);
        }

        var holder = defaultMap ?? Spawn(null, MapCoordinates.Nullspace);
        return (holder, EnsureComp<BankLedgerComponent>(holder));
    }

    /// <summary>
    /// Opens an account for a character, if they don't have one yet.
    /// </summary>
    public void EnsureAccount(string name, int balance = 0)
    {
        GetLedger().Comp.Accounts.TryAdd(name, balance);
    }

    public bool TryGetBalance(string name, out int balance)
    {
        balance = 0;
        return GetLedger().Comp.Accounts.TryGetValue(name, out balance);
    }

    public override bool TryGetBalance(EntityUid mob, out int balance)
    {
        return TryGetBalance(Name(mob), out balance);
    }

    /// <summary>
    /// The bank balance of the character a mob is, or 0 if they don't have an account.
    /// </summary>
    public int GetBalance(EntityUid mob)
    {
        return TryGetBalance(mob, out var balance) ? balance : 0;
    }

    public override bool TryBankWithdraw(EntityUid mob, int amount)
    {
        return TryBankWithdraw(Name(mob), amount);
    }

    /// <summary>
    /// Takes money out of a character's account.
    /// </summary>
    /// <returns>False, without taking anything, if they can't afford it or don't have an account.</returns>
    public bool TryBankWithdraw(string name, int amount)
    {
        var ledger = GetLedger();
        if (amount <= 0
            || !ledger.Comp.Accounts.TryGetValue(name, out var balance)
            || balance < amount)
        {
            return false;
        }

        SetBalance(ledger, name, balance - amount);
        return true;
    }

    public bool TryBankDeposit(EntityUid mob, int amount)
    {
        return TryBankDeposit(Name(mob), amount);
    }

    /// <summary>
    /// Pays money into a character's account, whether or not anyone is playing them. Opens one if they don't have
    /// one yet, so the money doesn't vanish.
    /// </summary>
    public bool TryBankDeposit(string name, int amount)
    {
        if (amount <= 0)
            return false;

        var ledger = GetLedger();
        SetBalance(ledger, name, ledger.Comp.Accounts.GetValueOrDefault(name) + amount);
        return true;
    }

    private void SetBalance(Entity<BankLedgerComponent> ledger, string name, int balance)
    {
        ledger.Comp.Accounts[name] = balance;

        // Keep whoever is playing them up to date.
        var query = EntityQueryEnumerator<BankAccountComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var account, out var meta))
        {
            if (meta.EntityName == name)
                SetMirroredBalance((uid, account), balance);
        }
    }
}
