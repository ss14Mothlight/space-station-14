using Content.Server._Mothlight.Persistence;
using Content.Server.GameTicking;
using Content.Shared._NullLink;
using Content.Shared.Humanoid;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Mothlight.Economy;

/// <summary>
/// Makes credits belong to characters rather than players. Starlight keeps a player's credits on their account; here
/// the account's "credits" resource just mirrors whichever character the player is controlling, and every change to
/// it is written back to that character.
/// </summary>
public sealed partial class CharacterCreditsSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = null!;
    [Dependency] private INetManager _net = null!;
    [Dependency] private ISharedPlayerManager _players = null!;
    [Dependency] private GameTicker _ticker = null!;
    [Dependency] private ISharedNullLinkPlayerResourcesManager _resources = null!;

    public const string CreditsResource = "credits";

    /// <summary>
    /// What a character starts out with.
    /// </summary>
    public const int StartingCredits = 100;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        // Only characters have money. Ghosts, observers and the like can't spend anyone's.
        var balance = 0;
        if (TryGetCredits(ev.Entity, out var credits))
        {
            ClaimPending(ev.Entity, credits);
            balance = credits.Balance;
        }

        _resources.TrySetResource(ev.Player, CreditsResource, balance, skipNullLink: true);
        SendResources(ev.Player);
    }

    /// <summary>
    /// Keeps the client's copy of their resources (which client-side code reads credits from) up to date.
    /// </summary>
    private void SendResources(ICommonSession session)
    {
        if (_resources.TryGetResources(session, out var resources))
            _net.ServerSendMessage(new MsgUpdatePlayerResources { Resources = resources }, session.Channel);
    }

    /// <summary>
    /// Gets a character's credits, opening an account for player characters that don't have one yet.
    /// </summary>
    private bool TryGetCredits(EntityUid mob, out CharacterCreditsComponent credits)
    {
        if (TryComp(mob, out credits!))
            return true;

        if (!HasComp<HumanoidAppearanceComponent>(mob) && !HasComp<PersistentCharacterComponent>(mob))
            return false;

        credits = AddComp<CharacterCreditsComponent>(mob);
        credits.Balance = StartingCredits;
        return true;
    }

    /// <summary>
    /// Called by the resource manager whenever a player's credits change, to write it to their character.
    /// </summary>
    public void OnCreditsChanged(ICommonSession session, double balance)
    {
        if (session.AttachedEntity is { } mob && TryGetCredits(mob, out var credits))
            credits.Balance = (int) balance;

        SendResources(session);
    }

    /// <summary>
    /// The credits of whoever the user is playing.
    /// </summary>
    public int GetBalance(EntityUid user)
    {
        return _resources.TryGetResource(user, CreditsResource, out var balance) ? (int) balance.Value : 0;
    }

    /// <summary>
    /// Adds to (or, if negative, takes from) the credits of whoever the user is playing.
    /// Fails without changing anything if they can't afford it.
    /// </summary>
    public bool TryChangeBalance(EntityUid user, int amount)
    {
        if (amount < 0 && GetBalance(user) < -amount)
            return false;

        return _resources.TryUpdateResource(user, CreditsResource, amount);
    }

    /// <summary>
    /// Pays a character by name, whether or not anyone is playing them right now.
    /// </summary>
    public void Deposit(string name, int amount)
    {
        if (amount <= 0)
            return;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } mob
                && HasComp<CharacterCreditsComponent>(mob)
                && Name(mob) == name
                && _resources.TryUpdateResource(session, CreditsResource, amount))
            {
                return;
            }
        }

        // Nobody's playing them, but their body might still be around.
        var query = EntityQueryEnumerator<CharacterCreditsComponent, MetaDataComponent>();
        while (query.MoveNext(out var credits, out var meta))
        {
            if (meta.EntityName != name)
                continue;

            credits.Balance += amount;
            return;
        }

        // Otherwise hold on to it until somebody plays them.
        if (GetPending() is { } pending)
            pending.Pending[name] = pending.Pending.GetValueOrDefault(name) + amount;
    }

    private void ClaimPending(EntityUid mob, CharacterCreditsComponent credits)
    {
        if (GetPending() is not { } pending || !pending.Pending.Remove(Name(mob), out var amount))
            return;

        credits.Balance += amount;
    }

    private PendingCreditsComponent? GetPending()
    {
        var query = EntityQueryEnumerator<PendingCreditsComponent>();
        if (query.MoveNext(out var existing))
            return existing;

        if (!_map.TryGetMap(_ticker.DefaultMap, out var map) || TerminatingOrDeleted(map))
            return null;

        return EnsureComp<PendingCreditsComponent>(map.Value);
    }
}
