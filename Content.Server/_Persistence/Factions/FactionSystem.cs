using System.Linq;
using Content.Server.Cargo.Components;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Server.Worldgen.Components.Debris;
using Content.Shared._Persistence.Factions;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// Server side of player-run factions: hands out faction ids, keeps the world-wide registry, and changes faction
/// membership. Ported from SS14-Persistence, where this was spread over StationSystem and CrewMetaRecordsSystem.
/// </summary>
public sealed partial class FactionSystem : SharedFactionSystem
{
    [Dependency] private GameTicker _ticker = null!;
    [Dependency] private SharedMapSystem _map = null!;
    [Dependency] private StationSystem _station = null!;
    [Dependency] private IPrototypeManager _proto = null!;

    /// <summary>
    /// The station prototype player-founded factions are made from.
    /// </summary>
    public static readonly EntProtoId FactionStationPrototype = "StandardPersistStation";

    /// <summary>
    /// Factions that started up before there was a map to keep the registry on.
    /// </summary>
    private readonly HashSet<EntityUid> _pendingRegistration = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionDataComponent, ComponentStartup>(OnFactionStartup);
        SubscribeLocalEvent<FactionDataComponent, ComponentShutdown>(OnFactionShutdown);
        SubscribeLocalEvent<StationGridAddedEvent>(OnGridAdded);

        InitializeStationModification();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingRegistration.Count == 0 || GetMetaRecords() is not { } meta)
            return;

        foreach (var station in _pendingRegistration)
        {
            if (TryComp<FactionDataComponent>(station, out var data) && !TerminatingOrDeleted(station))
                meta.Comp.Stations[data.UID] = station;
        }

        _pendingRegistration.Clear();
    }

    private void OnFactionStartup(Entity<FactionDataComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.UID == 0)
        {
            ent.Comp.UID = NextFactionId();
            Dirty(ent);
        }

        if (GetMetaRecords() is { } meta)
            meta.Comp.Stations[ent.Comp.UID] = ent;
        else
            _pendingRegistration.Add(ent);
    }

    private void OnFactionShutdown(Entity<FactionDataComponent> ent, ref ComponentShutdown args)
    {
        _pendingRegistration.Remove(ent);

        var query = EntityQueryEnumerator<CrewMetaRecordsComponent>();
        while (query.MoveNext(out var meta))
        {
            if (meta.Stations.TryGetValue(ent.Comp.UID, out var registered) && registered == ent.Owner)
                meta.Stations.Remove(ent.Comp.UID);
        }
    }

    private void OnGridAdded(StationGridAddedEvent ev)
    {
        // Claimed debris shouldn't get cleaned up by worldgen.
        if (HasComp<FactionDataComponent>(ev.Station))
            RemComp<OwnedDebrisComponent>(ev.GridId);
    }

    private int NextFactionId()
    {
        // Never reuse an id, cards and records referring to a deleted faction shouldn't end up pointing at a new one.
        var highest = 0;
        var query = EntityQueryEnumerator<FactionDataComponent>();
        while (query.MoveNext(out var data))
        {
            highest = Math.Max(highest, data.UID);
        }

        var metaQuery = EntityQueryEnumerator<CrewMetaRecordsComponent>();
        while (metaQuery.MoveNext(out var meta))
        {
            if (meta.Stations.Count > 0)
                highest = Math.Max(highest, meta.Stations.Keys.Max());
        }

        return highest + 1;
    }

    #region Registry

    /// <summary>
    /// Gets the world-wide faction records, creating them on the default map if they don't exist yet.
    /// Returns null if there's no default map (e.g. in the lobby).
    /// </summary>
    public Entity<CrewMetaRecordsComponent>? GetMetaRecords()
    {
        var query = EntityQueryEnumerator<CrewMetaRecordsComponent>();
        if (query.MoveNext(out var uid, out var existing))
            return (uid, existing);

        if (!_map.TryGetMap(_ticker.DefaultMap, out var map) || TerminatingOrDeleted(map))
            return null;

        return (map.Value, EnsureComp<CrewMetaRecordsComponent>(map.Value));
    }

    /// <summary>
    /// Gets a character's world-wide record, creating it if needed.
    /// </summary>
    public CrewMetaRecord? EnsureMetaRecord(string name)
    {
        if (GetMetaRecords() is not { } meta)
            return null;

        if (!meta.Comp.CrewMetaRecords.TryGetValue(name, out var record))
        {
            record = new CrewMetaRecord(name);
            meta.Comp.CrewMetaRecords.Add(name, record);
        }

        return record;
    }

    public bool TryGetMetaRecord(string name, out CrewMetaRecord? record)
    {
        record = null;
        return GetMetaRecords() is { } meta && meta.Comp.CrewMetaRecords.TryGetValue(name, out record);
    }

    #endregion

    #region Founding

    /// <summary>
    /// Founds a new faction with the given owner.
    /// </summary>
    public EntityUid CreateFaction(string name, string? owner)
    {
        var config = new StationConfig { StationPrototype = FactionStationPrototype };
        var station = _station.InitializeNewStation(config, null, name);
        if (owner != null)
            AddOwner(station, owner);

        return station;
    }

    #endregion

    #region Membership

    public void AddOwner(EntityUid station, string owner)
    {
        if (!TryComp<FactionDataComponent>(station, out var data) || data.Owners.Contains(owner))
            return;

        data.Owners.Add(owner);
        Dirty(station, data);
    }

    public void RemoveOwner(EntityUid station, string owner)
    {
        if (!TryComp<FactionDataComponent>(station, out var data) || !data.Owners.Remove(owner))
            return;

        Dirty(station, data);
    }

    /// <summary>
    /// Gets someone's record in the faction, creating an empty one if they don't have one.
    /// </summary>
    public CrewRecord? EnsureRecord(EntityUid station, string name)
    {
        if (string.IsNullOrEmpty(name) || !TryComp<CrewRecordsComponent>(station, out var records))
            return null;

        if (!records.CrewRecords.TryGetValue(name, out var record))
        {
            record = new CrewRecord(name) { LastPaid = DateTime.Now };
            records.CrewRecords.Add(name, record);
            Dirty(station, records);
        }

        return record;
    }

    public void SetAssignment(EntityUid station, CrewRecord record, int assignment)
    {
        record.AssignmentID = assignment;
        if (TryComp<CrewRecordsComponent>(station, out var records))
            Dirty(station, records);
    }

    public void ResetSpending(string userName, EntityUid station)
    {
        if (!TryGetRecord(userName, station, out var record))
            return;

        record.Spent = 0;
        Dirty(station, Comp<CrewRecordsComponent>(station));
    }

    /// <summary>
    /// Adds to how much of the faction's money someone has spent. Owners' spending isn't limited, so isn't tracked.
    /// </summary>
    public void TrackSpending(string userName, EntityUid station, int spent)
    {
        if (IsOwner(userName, station) || !TryGetRecord(userName, station, out var record))
            return;

        record.Spent += spent;
        Dirty(station, Comp<CrewRecordsComponent>(station));
    }

    #endregion

    #region Grids

    /// <summary>
    /// How many tiles the character has claimed as personal grids.
    /// </summary>
    public int GetPersonalTileCount(string realName)
    {
        var count = 0;
        var query = EntityQueryEnumerator<PersonalMemberComponent, MapGridComponent>();
        while (query.MoveNext(out var gridUid, out var member, out var grid))
        {
            if (member.OwnerName == realName)
                count += _map.GetAllTiles(gridUid, grid).Count();
        }

        return count;
    }

    /// <summary>
    /// How many tiles the faction has claimed.
    /// </summary>
    public int GetStationTileCount(EntityUid station)
    {
        var count = 0;
        var query = EntityQueryEnumerator<StationMemberComponent, MapGridComponent>();
        while (query.MoveNext(out var gridUid, out var member, out var grid))
        {
            if (member.Station == station)
                count += _map.GetAllTiles(gridUid, grid).Count();
        }

        return count;
    }

    /// <summary>
    /// The trade station the faction has claimed, if any.
    /// </summary>
    public EntityUid? GetStationTradeStation(EntityUid station)
    {
        var query = EntityQueryEnumerator<StationMemberComponent, TradeStationComponent>();
        while (query.MoveNext(out var gridUid, out var member, out _))
        {
            if (member.Station == station)
                return gridUid;
        }

        return null;
    }

    public void AddGridToPerson(string owner, EntityUid grid)
    {
        var member = EnsureComp<PersonalMemberComponent>(grid);
        member.OwnerName = owner;
        Dirty(grid, member);
        RemComp<OwnedDebrisComponent>(grid);
    }

    public void RemoveGridFromPerson(EntityUid grid)
    {
        RemComp<PersonalMemberComponent>(grid);
    }

    #endregion
}
