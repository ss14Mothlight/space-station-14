using Content.Server.Access.Systems;
using Content.Shared._Persistence.Factions.Components;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// Spending limits: on a player-run faction (one with owners), spending the faction's money at its consoles needs
/// an ID belonging to an owner, or to someone whose assignment's spending limit covers it.
/// Factions nobody owns, like the round's own station, spend as normal.
/// </summary>
public sealed partial class FactionSystem
{
    [Dependency] private IdCardSystem _idCard = null!;

    /// <summary>
    /// Whether the user may spend the amount of the console's faction's money.
    /// </summary>
    public bool CanSpendAt(EntityUid console, EntityUid user, int amount)
    {
        if (GetOwningFaction(console) is not { } station
            || !TryComp<FactionDataComponent>(station, out var data)
            || data.Owners.Count == 0)
        {
            return true;
        }

        return GetSpenderName(user) is { } name && CanSpend(name, station, amount);
    }

    /// <summary>
    /// Counts money spent at the console against the user's spending limit.
    /// </summary>
    public void RecordSpending(EntityUid console, EntityUid user, int amount)
    {
        if (GetOwningFaction(console) is { } station && GetSpenderName(user) is { } name)
            TrackSpending(name, station, amount);
    }

    private string? GetSpenderName(EntityUid user)
    {
        return _idCard.TryFindIdCard(user, out var id) ? id.Comp.FullName : null;
    }
}
