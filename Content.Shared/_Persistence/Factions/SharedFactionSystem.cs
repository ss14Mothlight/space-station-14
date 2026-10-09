using System.Diagnostics.CodeAnalysis;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared._Persistence.GridControl;
using Content.Shared.Access.Components;
using Content.Shared.PDA;
using Content.Shared.Station;
using Robust.Shared.Map.Components;

namespace Content.Shared._Persistence.Factions;

/// <summary>
/// Lookups for player-run factions. A faction is a station with <see cref="FactionDataComponent"/>; who is in it,
/// and what they can do, is keyed by character name.
/// Ported from SS14-Persistence, where most of this lived in SharedStationSystem.
/// </summary>
public abstract partial class SharedFactionSystem : EntitySystem
{
    [Dependency] private SharedStationSystem _station = null!;

    #region Lookups

    /// <summary>
    /// Finds the faction with the given <see cref="FactionDataComponent.UID"/>.
    /// </summary>
    public EntityUid? GetStationByID(int uid)
    {
        if (uid == 0)
            return null;

        var query = EntityQueryEnumerator<FactionDataComponent>();
        while (query.MoveNext(out var station, out var data))
        {
            if (data.UID == uid)
                return station;
        }

        return null;
    }

    public int GetStationID(EntityUid station)
    {
        return CompOrNull<FactionDataComponent>(station)?.UID ?? 0;
    }

    /// <summary>
    /// The faction the entity has been tagged for, or else the faction whose grid it's on.
    /// </summary>
    public EntityUid? GetOwningFaction(EntityUid entity)
    {
        if (TryComp<FactionTagComponent>(entity, out var tag) && GetStationByID(tag.FactionId) is { } tagged)
            return tagged;

        var station = _station.GetOwningStation(entity);
        return HasComp<FactionDataComponent>(station) ? station : null;
    }

    /// <summary>
    /// The character who personally claimed the grid the entity is on, if anyone.
    /// </summary>
    public string? GetOwningPerson(EntityUid entity, TransformComponent? xform = null)
    {
        if (!Resolve(entity, ref xform, false))
            return null;

        if (HasComp<MapGridComponent>(entity))
            return CompOrNull<PersonalMemberComponent>(entity)?.OwnerName;

        if (xform.GridUid is not { } grid)
            return null;

        return CompOrNull<PersonalMemberComponent>(grid)?.OwnerName;
    }

    /// <summary>
    /// Returns the owning faction and/or person for the specified entity.
    /// </summary>
    public void GetOwning(EntityUid uid, out EntityUid? owningStation, out string? owningPerson)
    {
        owningStation = GetOwningFaction(uid);
        owningPerson = owningStation == null ? GetOwningPerson(uid) : Name(owningStation.Value);
    }

    /// <summary>
    /// The tag a grid shows on radar, if it belongs to a player-run faction and hasn't been told to hide it.
    /// </summary>
    public string? GetIffTag(EntityUid grid)
    {
        if (HasComp<HiddenFactionTagComponent>(grid)
            || GetOwningFaction(grid) is not { } station
            || !TryComp<FactionDataComponent>(station, out var data)
            || data.Owners.Count == 0)
        {
            return null;
        }

        var tag = data.GetResolvedFactionTag(Name(station));
        return string.IsNullOrEmpty(tag) ? null : tag;
    }

    public List<EntityUid> GetFactions()
    {
        var factions = new List<EntityUid>();
        var query = EntityQueryEnumerator<FactionDataComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            factions.Add(uid);
        }

        return factions;
    }

    /// <summary>
    /// Every faction the character owns or has a record in.
    /// </summary>
    public List<EntityUid> GetStationsAvailableTo(string realName)
    {
        var stations = new List<EntityUid>();
        var query = EntityQueryEnumerator<FactionDataComponent>();
        while (query.MoveNext(out var uid, out var data))
        {
            if (data.IsOwner(realName) || HasRecord(realName, uid))
                stations.Add(uid);
        }

        return stations;
    }

    /// <summary>
    /// Gets the character name an ID card or PDA is registered to.
    /// </summary>
    public string? GetIdName(EntityUid item)
    {
        if (TryComp<IdCardComponent>(item, out var id) && !string.IsNullOrEmpty(id.FullName))
            return id.FullName;

        if (TryComp<PdaComponent>(item, out var pda)
            && TryComp(pda.ContainedId, out id)
            && !string.IsNullOrEmpty(id.FullName))
        {
            return id.FullName;
        }

        return null;
    }

    /// <summary>
    /// Gets the character name from the first ID among the given access items.
    /// </summary>
    public string? GetIdName(IEnumerable<EntityUid> items)
    {
        foreach (var item in items)
        {
            if (GetIdName(item) is { } name)
                return name;
        }

        return null;
    }

    #endregion

    #region Membership

    public bool IsOwner(string userName, EntityUid station)
    {
        return CompOrNull<FactionDataComponent>(station)?.IsOwner(userName) ?? false;
    }

    public bool HasRecord(string userName, EntityUid station)
    {
        if (IsOwner(userName, station))
            return true;

        return CompOrNull<CrewRecordsComponent>(station)?.CrewRecords.ContainsKey(userName) ?? false;
    }

    public bool TryGetRecord(string userName, EntityUid station, [NotNullWhen(true)] out CrewRecord? record)
    {
        record = null;
        return TryComp<CrewRecordsComponent>(station, out var records) && records.TryGetRecord(userName, out record);
    }

    /// <summary>
    /// Gets the assignment the character currently holds in the faction.
    /// </summary>
    public bool TryGetAssignment(string userName,
        EntityUid station,
        [NotNullWhen(true)] out CrewAssignment? assignment)
    {
        assignment = null;
        return TryGetRecord(userName, station, out var record)
               && TryComp<CrewAssignmentsComponent>(station, out var assignments)
               && assignments.TryGetAssignment(record.AssignmentID, out assignment);
    }

    /// <summary>
    /// The accesses the character's assignment grants on the faction's grids.
    /// </summary>
    public IReadOnlyCollection<string> GetGrantedAccesses(string userName, EntityUid station)
    {
        return TryGetAssignment(userName, station, out var assignment) ? assignment.AccessIDs : [];
    }

    /// <summary>
    /// Filters a list of accesses down to the ones the faction has defined.
    /// </summary>
    public List<string> GetValidAccesses(IEnumerable<string> baseAccess, EntityUid station)
    {
        var valid = new List<string>();
        if (!TryComp<CrewAccessesComponent>(station, out var accesses))
            return valid;

        foreach (var access in baseAccess)
        {
            if (accesses.CrewAccesses.Contains(access))
                valid.Add(access);
        }

        return valid;
    }

    public bool CanEditGeneralRecord(string userName, EntityUid station)
    {
        if (IsOwner(userName, station))
            return true;

        return TryGetAssignment(userName, station, out var assignment) && assignment.CanEditGeneralRecord;
    }

    public bool CanAssign(string userName, EntityUid station)
    {
        if (IsOwner(userName, station))
            return true;

        return TryGetAssignment(userName, station, out var assignment) && assignment.CanAssign;
    }

    /// <summary>
    /// Whether the character may spend the given amount of the faction's money. Owners always can, everyone else
    /// is held to their assignment's spending limit.
    /// </summary>
    public bool CanSpend(string userName, EntityUid station, int toSpend = 0)
    {
        if (IsOwner(userName, station))
            return true;

        if (!TryGetRecord(userName, station, out var record)
            || !TryComp<CrewAssignmentsComponent>(station, out var assignments)
            || !assignments.TryGetAssignment(record.AssignmentID, out var assignment))
        {
            return false;
        }

        if (toSpend <= 0)
            return true;

        return assignment.SpendingLimit - record.Spent >= toSpend;
    }

    /// <summary>
    /// Whether the character may claim, unclaim or configure a grid belonging to the given owner.
    /// Factions allow their owners and anyone whose assignment can claim, personal grids only their owner, and
    /// unclaimed grids anyone.
    /// </summary>
    public bool GetGridAccess(EntityUid? grid,
        string privilegedName,
        EntityUid? owningStation,
        string? owningPerson,
        bool ignoreGrid = false)
    {
        if (string.IsNullOrEmpty(privilegedName))
            return false;

        if (owningStation != null)
        {
            if (IsOwner(privilegedName, owningStation.Value))
                return true;

            return TryGetAssignment(privilegedName, owningStation.Value, out var assignment) && assignment.CanClaim;
        }

        if (owningPerson != null)
            return owningPerson == privilegedName;

        return grid != null || ignoreGrid;
    }

    #endregion
}
