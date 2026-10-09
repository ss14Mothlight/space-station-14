using Content.Server.Access.Systems;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Access.Components;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// Keeps ID cards' job titles in line with their holder's faction employment, see <see cref="FactionIdCardComponent"/>.
/// </summary>
public sealed partial class FactionIdCardSystem : EntitySystem
{
    [Dependency] private FactionSystem _faction = null!;
    [Dependency] private IdCardSystem _idCard = null!;

    /// <summary>
    /// Makes a card registered to the given character, and ties it to their faction employment.
    /// </summary>
    public void BuildID(EntityUid card, string name, int factionId = 0)
    {
        if (!TryComp<IdCardComponent>(card, out var id))
            return;

        _idCard.TryChangeFullName(card, name, id);
        var factionCard = EnsureComp<FactionIdCardComponent>(card);
        factionCard.CreatedTime = DateTime.Now;
        factionCard.FactionId = factionId;
        factionCard.ManualTitle = false;
        Dirty(card, factionCard);
        RebuildJob((card, factionCard, id));
    }

    /// <summary>
    /// Points every card registered to the character at the faction they're now working for (0 for none).
    /// </summary>
    public void UpdateIDAssignment(string name, int factionId)
    {
        var query = EntityQueryEnumerator<IdCardComponent>();
        while (query.MoveNext(out var uid, out var id))
        {
            if (id.FullName != name)
                continue;

            var factionCard = EnsureComp<FactionIdCardComponent>(uid);
            factionCard.FactionId = factionId;
            factionCard.ManualTitle = false;
            Dirty(uid, factionCard);
            RebuildJob((uid, factionCard, id));
        }
    }

    /// <summary>
    /// Updates every card working for the faction, e.g. after its tag or an assignment's name changed.
    /// </summary>
    public void RefreshStationIds(int factionId)
    {
        var query = EntityQueryEnumerator<FactionIdCardComponent, IdCardComponent>();
        while (query.MoveNext(out var uid, out var factionCard, out var id))
        {
            if (factionCard.FactionId == factionId)
                RebuildJob((uid, factionCard, id));
        }
    }

    /// <summary>
    /// Updates the character's cards working for the faction, e.g. after their assignment changed.
    /// </summary>
    public void RefreshIds(string name, int factionId)
    {
        var query = EntityQueryEnumerator<FactionIdCardComponent, IdCardComponent>();
        while (query.MoveNext(out var uid, out var factionCard, out var id))
        {
            if (id.FullName == name && factionCard.FactionId == factionId)
                RebuildJob((uid, factionCard, id));
        }
    }

    /// <summary>
    /// Deletes every printed card registered to the character, so a newly printed one is the only valid one.
    /// </summary>
    public void ExpireAllIds(string name)
    {
        var now = DateTime.Now;
        var query = EntityQueryEnumerator<FactionIdCardComponent, IdCardComponent>();
        while (query.MoveNext(out var uid, out var factionCard, out var id))
        {
            if (id.FullName == name && factionCard.CreatedTime < now)
                QueueDel(uid);
        }
    }

    /// <summary>
    /// Stops the faction title from overwriting a job title written at an ID card console.
    /// </summary>
    public void MarkManualTitle(EntityUid card)
    {
        if (!TryComp<FactionIdCardComponent>(card, out var factionCard) || factionCard.ManualTitle)
            return;

        factionCard.ManualTitle = true;
        Dirty(card, factionCard);
    }

    public void RebuildJob(Entity<FactionIdCardComponent, IdCardComponent> card)
    {
        if (card.Comp1.ManualTitle)
            return;

        _idCard.TryChangeJobTitle(card, GetJobTitle(card.Comp2.FullName, card.Comp1.FactionId), card.Comp2);
    }

    /// <summary>
    /// The title a card shows for someone working for the faction: "[TAG] Assignment", or off duty.
    /// </summary>
    public string GetJobTitle(string? name, int factionId)
    {
        var offDuty = Loc.GetString("faction-id-card-off-duty");
        if (name == null
            || _faction.GetStationByID(factionId) is not { } station
            || !_faction.TryGetAssignment(name, station, out var assignment))
        {
            return offDuty;
        }

        var tag = Comp<FactionDataComponent>(station).GetResolvedFactionTag(Name(station));
        return string.IsNullOrEmpty(tag) ? assignment.Name : $"[{tag}] {assignment.Name}";
    }
}
