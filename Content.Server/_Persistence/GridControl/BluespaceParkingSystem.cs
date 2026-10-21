using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.Server._Mothlight.Persistence;
using Content.Server._Persistence.Factions;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Persistence.CCVar;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared._Persistence.GridControl;
using Content.Shared.Administration.Logs;
using Content.Shared.Cargo.Components;
using Content.Shared.Database;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Persistence.GridControl;

/// <summary>
/// Bluespace parking, ported from SS14-Persistence: an anchor key saves the grid it's on to a file and takes it out of
/// the world, then later brings it back near where it was parked.
/// </summary>
public sealed partial class BluespaceParkingSystem : SharedBluespaceParkingSystem
{
    [Dependency] private ISharedAdminLogManager _adminLog = null!;
    [Dependency] private SharedAudioSystem _audio = null!;
    [Dependency] private IConfigurationManager _cfg = null!;
    [Dependency] private CharacterPersistenceSystem _characters = null!;
    [Dependency] private IChatManager _chat = null!;
    [Dependency] private SharedContainerSystem _container = null!;
    [Dependency] private DockingSystem _dock = null!;
    [Dependency] private FactionSystem _faction = null!;
    [Dependency] private MapLoaderSystem _loader = null!;
    [Dependency] private SharedMapSystem _map = null!;
    [Dependency] private MetaDataSystem _metaData = null!;
    [Dependency] private PopupSystem _popup = null!;
    [Dependency] private IPrototypeManager _proto = null!;
    [Dependency] private IRobustRandom _random = null!;
    [Dependency] private IResourceManager _res = null!;
    [Dependency] private StationSystem _station = null!;
    [Dependency] private IGameTiming _timing = null!;
    [Dependency] private SharedTransformSystem _transform = null!;
    [Dependency] private UserInterfaceSystem _ui = null!;

    /// <summary>
    /// Where parked grids are saved, in the server's user data.
    /// </summary>
    public static readonly ResPath SaveRoot = new("/Mothlight/Parking");

    private static readonly ResPath BackupRoot = SaveRoot / "Backups";

    private static readonly SoundSpecifier StartupSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_begin.ogg")
    {
        Params = AudioParams.Default.WithVolume(-5f),
    };

    private static readonly SoundSpecifier UnparkingSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_end.ogg")
    {
        Params = AudioParams.Default.WithVolume(-5f),
    };

    [GeneratedRegex("[^a-zA-Z0-9 -]")]
    private static partial Regex SafeGridNameRegex();

    private List<Entity<MapGridComponent>> _grids = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BSPParkingTargetComponent, MoveEvent>(OnTargetMove);
        SubscribeLocalEvent<PersistenceSavesWipedEvent>(OnSavesWiped);

        Subs.BuiEvents<BSPAnchorKeyComponent>(BSPAnchorKeyUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>((uid, comp, _) => UpdateUserInterface((uid, comp)));
                subs.Event<BSPAnchorKeyStartPark>((uid, comp, _) => StartPark((uid, comp)));
                subs.Event<BSPAnchorKeyStartUnpark>((uid, comp, _) => StartUnpark((uid, comp)));
                subs.Event<BSPAnchorKeyCancel>((uid, comp, _) => CancelRoutine((uid, comp), Loc.GetString("bsp-cancel-user")));
                subs.Event<BSPAnchorKeyToggleClearOwnership>((uid, comp, _) => ToggleClearOwnership((uid, comp)));
            });
    }

    protected override void OnIdSlotChanged(Entity<BSPAnchorKeyComponent> ent)
    {
        base.OnIdSlotChanged(ent);
        UpdateUserInterface(ent);
    }

    private void OnTargetMove(Entity<BSPParkingTargetComponent> ent, ref MoveEvent args)
    {
        if (TryComp<BSPAnchorKeyComponent>(ent.Comp.Source, out var key))
            CancelRoutine((ent.Comp.Source, key), Loc.GetString("bsp-cancel-moved"));
    }

    private void OnSavesWiped(ref PersistenceSavesWipedEvent ev)
    {
        if (_res.UserData.Exists(SaveRoot))
            _res.UserData.Delete(SaveRoot);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var parkDelay = TimeSpan.FromSeconds(_cfg.GetCVar(PersistenceCCVars.BluespaceParkingParkDelay));
        var targets = EntityQueryEnumerator<BSPParkingTargetComponent>();
        while (targets.MoveNext(out var uid, out var target))
        {
            if (!TryComp<BSPAnchorKeyComponent>(target.Source, out var key) || key.State != BSPState.Parking)
            {
                RemCompDeferred<BSPParkingTargetComponent>(uid);
                continue;
            }

            var remaining = key.RoutineStartTime.GetValueOrDefault() + parkDelay - _timing.CurTime;
            if (!target.RampingUp && remaining.TotalSeconds <= 8)
            {
                target.RampingUp = true;
                var audio = _audio.PlayPvs(StartupSound, uid);
                _audio.SetGridAudio(audio);
                target.StartupStream = audio?.Entity;
            }
            else if (remaining <= TimeSpan.Zero && !CommitPark((target.Source, key), (uid, target)))
            {
                CancelRoutine((target.Source, key), Loc.GetString("bsp-cancel-park-failed"));
            }
        }

        var unparkDelay = TimeSpan.FromSeconds(_cfg.GetCVar(PersistenceCCVars.BluespaceParkingUnparkDelay));
        var unparks = EntityQueryEnumerator<BSPAnchorKeyUnparkingComponent, BSPAnchorKeyComponent>();
        while (unparks.MoveNext(out var uid, out var unparking, out var key))
        {
            if (key.State != BSPState.Unparking)
            {
                RemCompDeferred<BSPAnchorKeyUnparkingComponent>(uid);
                continue;
            }

            if (key.RoutineStartTime.GetValueOrDefault() + unparkDelay - _timing.CurTime <= TimeSpan.Zero
                && !CommitUnpark((uid, key), unparking))
            {
                CancelRoutine((uid, key), Loc.GetString("bsp-cancel-unpark-failed"));
            }
        }
    }

    #region Routines

    private void StartPark(Entity<BSPAnchorKeyComponent> ent)
    {
        var state = GetUIState(ent);
        if (state.State != BSPState.Idle || !state.CanAct || _transform.GetGrid(ent.Owner) is not { } grid)
            return;

        ent.Comp.State = BSPState.Parking;
        ent.Comp.CurrentTarget = grid;
        ent.Comp.RoutineStartTime = _timing.CurTime;
        EnsureComp<BSPParkingTargetComponent>(grid).Source = ent;

        UpdateUserInterface(ent);
        _adminLog.Add(LogType.Action, LogImpact.Low, $"Started bluespace parking {ToPrettyString(grid)} with {ToPrettyString(ent)}");
    }

    private void StartUnpark(Entity<BSPAnchorKeyComponent> ent)
    {
        var state = GetUIState(ent);
        if (state.State != BSPState.Parked || !state.CanAct)
            return;

        var position = _transform.GetWorldPosition(ent);
        if ((ent.Comp.SavedParkedPosition.GetValueOrDefault() - position).Length() >
            _cfg.GetCVar(PersistenceCCVars.BluespaceUnparkMaxDistance))
        {
            return;
        }

        var rotation = _transform.GetWorldRotation(ent);
        GetRecallSource(ent, position, rotation.Opposite(), out var mapId, out var origin, out var bounds, out var angle);
        if (!TryGetUnparkPlacementLocation(mapId, origin, bounds, angle, out _, out _, spawnDistance: 2, maxIterations: 4))
        {
            _popup.PopupEntity(Loc.GetString("bsp-unpark-crowded"), ent, PopupType.MediumCaution);
            return;
        }

        ent.Comp.RoutineStartTime = _timing.CurTime;
        ent.Comp.State = BSPState.Unparking;
        var unparking = EnsureComp<BSPAnchorKeyUnparkingComponent>(ent);
        unparking.Origin = position;
        unparking.Rotation = rotation;

        UpdateUserInterface(ent);
        _adminLog.Add(LogType.Action, LogImpact.Low, $"Started unparking '{ent.Comp.SavedGridName}' with {ToPrettyString(ent)}");
    }

    private void CancelRoutine(Entity<BSPAnchorKeyComponent> ent, string reason)
    {
        var state = ent.Comp.State;
        switch (state)
        {
            case BSPState.Parking:
                if (TryComp<BSPParkingTargetComponent>(ent.Comp.CurrentTarget, out var target))
                {
                    _audio.Stop(target.StartupStream);
                    RemCompDeferred<BSPParkingTargetComponent>(ent.Comp.CurrentTarget.Value);
                }

                ent.Comp.CurrentTarget = null;
                ent.Comp.State = BSPState.Idle;
                break;

            case BSPState.Unparking:
                RemCompDeferred<BSPAnchorKeyUnparkingComponent>(ent);
                ent.Comp.State = BSPState.Parked;
                break;

            default:
                return;
        }

        ent.Comp.RoutineStartTime = null;
        _popup.PopupEntity(Loc.GetString("bsp-cancelled", ("reason", reason)), ent, PopupType.MediumCaution);
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{state} '{ent.Comp.SavedGridName}' with {ToPrettyString(ent)} was cancelled: {reason}");
        UpdateUserInterface(ent);
    }

    private void ToggleClearOwnership(Entity<BSPAnchorKeyComponent> ent)
    {
        if (ent.Comp.State != BSPState.Parked)
            return;

        ent.Comp.SavedClearOwnership = !ent.Comp.SavedClearOwnership;
        UpdateUserInterface(ent);
    }

    #endregion

    #region Parking

    private bool CommitPark(Entity<BSPAnchorKeyComponent> key, Entity<BSPParkingTargetComponent> target)
    {
        var grid = target.Owner;
        if (key.Comp.State != BSPState.Parking
            || TerminatingOrDeleted(grid)
            || !TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            return false;
        }

        var now = DateTime.Now;
        var gridName = Name(grid);
        var filename = $"{now:yyyy-MM-dd_HH.mm.ss}_{SafeGridNameRegex().Replace(gridName, "")}_{Guid.NewGuid()}.yml";
        var (position, rotation) = _transform.GetWorldPositionRotation(grid);
        var bounds = mapGrid.LocalAABB;
        var tiles = _map.GetAllTiles(grid, mapGrid).Count();

        var station = _faction.GetOwningFaction(grid);
        var person = station == null ? _faction.GetOwningPerson(grid) : null;

        _dock.UndockDocks(grid);
        MoveStartupAudio(target, position, rotation, mapGrid);
        EvacuateGrid((grid, mapGrid), key);

        // Station entities aren't part of the grid, so take it off the station instead of saving a reference to it.
        // It's put back on when it's unparked.
        if (station != null)
            _station.RemoveGridFromStation(station.Value, grid);

        if (!TrySaveGrid(grid, SaveRoot / filename))
        {
            if (station != null)
                _station.AddGridToStation(station.Value, grid);

            _adminLog.Add(LogType.Action, LogImpact.High, $"Failed to bluespace park {ToPrettyString(grid)}");
            return false;
        }

        QueueDel(grid);

        key.Comp.CurrentTarget = null;
        key.Comp.SavedClearOwnership = false;
        key.Comp.SavedTimestamp = now;
        key.Comp.SavedFilename = filename;
        key.Comp.SavedOwnerFaction = station is { } owner ? _faction.GetStationID(owner) : null;
        key.Comp.SavedOwnerPersonal = person;
        key.Comp.SavedParkedBounds = bounds;
        key.Comp.SavedParkedPosition = position;
        key.Comp.SavedParkedRotation = rotation;
        key.Comp.SavedTileCount = tiles;
        key.Comp.SavedGridName = gridName;
        key.Comp.State = BSPState.Parked;
        UpdateAppearance(key);

        _popup.PopupEntity(Loc.GetString("bsp-parked", ("grid", gridName)), key);
        _metaData.SetEntityName(key, Loc.GetString("bsp-key-name-parked", ("name", Prototype(key)?.Name ?? Name(key)), ("grid", gridName)));
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Bluespace parked '{gridName}' ({grid}) with {ToPrettyString(key)} as {filename}");

        UpdateUserInterface(key);
        return true;
    }

    private bool TrySaveGrid(EntityUid grid, ResPath path)
    {
        _res.UserData.CreateDir(path.Directory);

        // Anything outside the grid (stations, other grids) isn't coming along, so drop references to it.
        var opts = SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            ErrorOnOrphan = false,
        };

        // Bodies lying around on the grid come along, they just don't usually get saved in maps.
        using (_characters.AllowHumanoidSaving())
        {
            return _loader.TrySaveGrid(grid, path, opts);
        }
    }

    /// <summary>
    /// Keeps the startup sound playing where the grid was, after it's gone.
    /// </summary>
    private void MoveStartupAudio(BSPParkingTargetComponent target, Vector2 position, Angle rotation, MapGridComponent grid)
    {
        if (target.StartupStream is not { } stream || !TryComp<AudioComponent>(stream, out var audio))
            return;

        var offset = position + rotation.Opposite().ToVec() * grid.LocalAABB.MaxDimension;
        var mapUid = Transform(target.Owner).MapUid;
        if (mapUid == null)
            return;

        var clipped = _audio.PlayStatic(StartupSound, Filter.Broadcast(), new EntityCoordinates(mapUid.Value, offset), true, audio.Params);
        _audio.SetPlaybackPosition(clipped, (float) ((audio.PauseTime ?? _timing.CurTime) - audio.AudioStart).TotalSeconds);
        _audio.Stop(stream);
    }

    /// <summary>
    /// Gets people off a grid that's about to be parked. Players are put out in space next to it. Characters nobody's
    /// playing are put away in their save files, as if they'd gone into cryo, since they can't float in space.
    /// </summary>
    /// <param name="key">The anchor key, which has to stay behind to bring the grid back.</param>
    private void EvacuateGrid(Entity<MapGridComponent> grid, EntityUid? key = null)
    {
        var toEvacuate = new List<EntityUid>();
        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mind, out var xform))
        {
            if (xform.GridUid == grid.Owner
                && (mind.HasMind || HasComp<ActorComponent>(uid) || HasComp<PersistentCharacterComponent>(uid)))
            {
                toEvacuate.Add(uid);
            }
        }

        // Unless someone being evacuated is carrying it.
        if (key is { } keyUid
            && Transform(keyUid).GridUid == grid.Owner
            && !toEvacuate.Any(uid => _transform.IsParentOf(Transform(uid), keyUid)))
        {
            toEvacuate.Add(keyUid);
        }

        var gridXform = Transform(grid);
        var mapUid = gridXform.MapUid;
        var gridRotation = _transform.GetWorldRotation(gridXform);
        var worldMatrix = _transform.GetWorldMatrix(gridXform);
        var worldBounds = worldMatrix.TransformBox(grid.Comp.LocalAABB).Enlarged(0.2f);

        foreach (var uid in toEvacuate)
        {
            if (!HasComp<ActorComponent>(uid) && TryComp<PersistentCharacterComponent>(uid, out var character))
            {
                _characters.TrySaveCharacter((uid, character));
                _transform.DetachEntity(uid, Transform(uid));
                QueueDel(uid);
                continue;
            }

            if (mapUid == null)
                continue;

            _container.TryRemoveFromContainer(uid, force: true);
            var position = _transform.GetWorldPosition(uid);
            var direction = position - worldBounds.Center;
            if (direction.EqualsApprox(Vector2.Zero))
                direction = _random.NextAngle().ToVec();

            var outside = worldBounds.ClosestPoint(position + direction.Normalized() * worldBounds.MaxDimension);
            _transform.SetCoordinates(uid, new EntityCoordinates(mapUid.Value, outside));
            _transform.SetWorldRotation(uid, gridRotation + Transform(uid).LocalRotation);

            if (TryComp<ActorComponent>(uid, out var actor))
                _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString("bsp-evacuated"));
        }
    }

    #endregion

    #region Unparking

    private bool CommitUnpark(Entity<BSPAnchorKeyComponent> key, BSPAnchorKeyUnparkingComponent unparking)
    {
        if (key.Comp.State != BSPState.Unparking || key.Comp.SavedFilename is not { } filename)
            return false;

        var path = SaveRoot / filename;
        if (!_res.UserData.Exists(path))
            return false;

        GetRecallSource(key, unparking.Origin, unparking.Rotation.Opposite(), out var mapId, out var origin, out var bounds, out var angle);
        if (!TryGetUnparkPlacementLocation(mapId, origin, bounds, angle, out var coords, out var rotation))
            return false;

        var opts = DeserializationOptions.Default with { LogInvalidEntities = false };
        if (!_loader.TryLoadGrid(mapId, path, out var loaded, opts, coords.Position, rotation))
        {
            _adminLog.Add(LogType.Action, LogImpact.High, $"Failed to unpark '{key.Comp.SavedGridName}' from {path} with {ToPrettyString(key)}");
            return false;
        }

        var grid = loaded.Value.Owner;
        _transform.SetWorldPositionRotation(grid, coords.Position, rotation);

        // Keep the old save around, in case something went wrong.
        _res.UserData.CreateDir(BackupRoot);
        _res.UserData.Rename(path, BackupRoot / filename);

        if (key.Comp.SavedClearOwnership)
            _faction.RemoveGridFromPerson(grid);
        else if (key.Comp.SavedOwnerFaction is { } factionId && _faction.GetStationByID(factionId) is { } station)
            _station.AddGridToStation(station, grid);

        // Whatever was floating where the grid came back shouldn't end up stuck in its walls.
        Timer.Spawn(10, () =>
        {
            if (TryComp<MapGridComponent>(grid, out var mapGrid))
                EvacuateGrid((grid, mapGrid));
        });

        TryDock(grid, new MapCoordinates(unparking.Origin, mapId));

        var audio = _audio.PlayPvs(UnparkingSound, grid);
        _audio.SetGridAudio(audio);

        var offset = _transform.GetWorldPosition(grid) - _transform.GetWorldPosition(key);
        _popup.PopupEntity(Loc.GetString("bsp-unparked",
                ("grid", key.Comp.SavedGridName ?? Name(grid)),
                ("distance", (int) offset.Length()),
                ("direction", offset.GetDir().ToString())),
            key);
        _metaData.SetEntityName(key, Prototype(key)?.Name ?? Name(key));
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Unparked '{key.Comp.SavedGridName}' as {ToPrettyString(grid)} with {ToPrettyString(key)}");

        key.Comp.SavedTimestamp = null;
        key.Comp.SavedFilename = null;
        key.Comp.SavedOwnerFaction = null;
        key.Comp.SavedOwnerPersonal = null;
        key.Comp.SavedParkedBounds = null;
        key.Comp.SavedParkedPosition = null;
        key.Comp.SavedParkedRotation = null;
        key.Comp.SavedTileCount = null;
        key.Comp.SavedGridName = null;
        key.Comp.SavedClearOwnership = false;
        key.Comp.RoutineStartTime = null;
        key.Comp.State = BSPState.Idle;
        RemCompDeferred<BSPAnchorKeyUnparkingComponent>(key);
        UpdateAppearance(key);
        UpdateUserInterface(key);
        return true;
    }

    /// <summary>
    /// Docks the unparked grid to whatever grid the key was on, if any of their docks line up.
    /// </summary>
    private void TryDock(EntityUid grid, MapCoordinates keyPosition)
    {
        if (!_map.TryFindGridAt(keyPosition, out var other, out _))
            return;

        Timer.Spawn(20, () =>
        {
            if (TerminatingOrDeleted(grid) || TerminatingOrDeleted(other))
                return;

            foreach (var dock in _dock.GetDocks(grid))
            {
                foreach (var otherDock in _dock.GetDocks(other))
                {
                    if (dock.Comp.Docked || otherDock.Comp.Docked || !_dock.CanDock(dock, otherDock))
                        continue;

                    _dock.Dock(dock, otherDock);
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Works out where to bring a grid back so it appears next to the key, rather than on top of it.
    /// </summary>
    private void GetRecallSource(Entity<BSPAnchorKeyComponent> key,
        Vector2 keyPosition,
        Angle keyRotation,
        out MapId mapId,
        out Vector2 origin,
        out Box2 bounds,
        out Angle angle)
    {
        mapId = _transform.GetMapId(key.Owner);
        bounds = key.Comp.SavedParkedBounds.GetValueOrDefault();
        angle = keyRotation;
        var offsetPoint = bounds.ClosestPoint(bounds.Center + angle.Opposite().ToVec() * bounds.MaxDimension);
        var offset = (angle + MathF.PI / 2).ToVec() * (-1f + (offsetPoint - bounds.Center).Length());
        origin = keyPosition + offset;
    }

    /// <summary>
    /// Finds a spot near <paramref name="origin"/> with nothing in the way, heading further out (and turning
    /// every so often) until one turns up.
    /// </summary>
    private bool TryGetUnparkPlacementLocation(MapId mapId,
        Vector2 origin,
        Box2 bounds,
        Angle worldAngle,
        out MapCoordinates coords,
        out Angle angle,
        float spawnDistance = 0,
        int maxIterations = 1000)
    {
        angle = worldAngle;
        var direction = worldAngle;
        for (var i = 0; i < maxIterations; i++)
        {
            // Turn a corner every so often instead of heading off forever.
            if (i > 0 && i % 250 == 0)
            {
                direction += Angle.FromDegrees(90);
                spawnDistance = 0;
            }

            var position = origin + (direction + Math.PI / 2).ToVec() * spawnDistance;
            var box = Box2.CenteredAround(position + bounds.Center, bounds.Size);
            var rotated = new Box2Rotated(box, angle, position);

            _grids.Clear();
            _map.FindGridsIntersecting(mapId, rotated, ref _grids);
            if (_grids.Count == 0)
            {
                coords = new MapCoordinates(position, mapId);
                return true;
            }

            spawnDistance += 10;
        }

        coords = MapCoordinates.Nullspace;
        return false;
    }

    #endregion

    #region UI

    private BSPAnchorKeyBoundUserInterfaceState GetUIState(Entity<BSPAnchorKeyComponent> ent)
    {
        var comp = ent.Comp;
        var parked = comp.State is BSPState.Parked or BSPState.Unparking;
        var state = new BSPAnchorKeyBoundUserInterfaceState
        {
            State = comp.State,
            RoutineStartTime = comp.RoutineStartTime,
            ParkDelay = _cfg.GetCVar(PersistenceCCVars.BluespaceParkingParkDelay),
            UnparkDelay = _cfg.GetCVar(PersistenceCCVars.BluespaceParkingUnparkDelay),
            ParkWorldPosition = comp.SavedParkedPosition,
            UnparkMaxDistance = _cfg.GetCVar(PersistenceCCVars.BluespaceUnparkMaxDistance),
            ClearOwnership = comp.SavedClearOwnership,
            TileLimit = _cfg.GetCVar(PersistenceCCVars.BluespaceParkingMaxTiles),
        };

        string? privilegedName = null;
        if (comp.PrivilegedIdSlot.Item is { } id)
        {
            state.IdName = Name(id);
            privilegedName = _faction.GetIdName(id);
        }

        EntityUid? grid = null;
        EntityUid? owningStation;
        string? owningPerson;
        if (parked)
        {
            state.GridName = comp.SavedGridName;
            state.GridTileCount = comp.SavedTileCount ?? 0;
            owningStation = comp.SavedOwnerFaction is { } factionId ? _faction.GetStationByID(factionId) : null;
            owningPerson = comp.SavedOwnerPersonal;
        }
        else
        {
            grid = comp.CurrentTarget ?? _transform.GetGrid(ent.Owner);
            if (grid is { } current && TryComp<MapGridComponent>(current, out var mapGrid))
            {
                state.GridName = Name(current);
                state.GridTileCount = _map.GetAllTiles(current, mapGrid).Count();
            }

            owningStation = grid is { } g ? _faction.GetOwningFaction(g) : null;
            owningPerson = grid is { } p && owningStation == null ? _faction.GetOwningPerson(p) : null;
        }

        state.OwnerName = owningStation is { } stationName ? Name(stationName) : owningPerson;
        state.IsAuth = privilegedName != null
                       && _faction.GetGridAccess(grid, privilegedName, owningStation, owningPerson, ignoreGrid: parked);

        // How many tiles the owner already has, for when this grid comes back and counts towards their limit again.
        if (owningStation is { } station)
        {
            state.GridOwnerTotalTiles = _faction.GetStationTileCount(station);
            if (_proto.Resolve(Comp<FactionDataComponent>(station).Level, out var level))
                state.TileLimit = level.TileLimit;
        }
        else if (owningPerson != null)
        {
            state.GridOwnerTotalTiles = _faction.GetPersonalTileCount(owningPerson);
            if (_faction.TryGetMetaRecord(owningPerson, out var record) && _proto.Resolve(record!.Level, out var networkLevel))
                state.TileLimit = networkLevel.TileLimit;
        }

        state.ErrorMessage = GetError(state, grid, parked);
        return state;
    }

    private string? GetError(BSPAnchorKeyBoundUserInterfaceState state, EntityUid? grid, bool parked)
    {
        if (parked)
        {
            if (!state.ClearOwnership
                && state.OwnerName != null
                && state.GridTileCount + state.GridOwnerTotalTiles > state.TileLimit)
            {
                return Loc.GetString("bsp-error-tile-limit");
            }

            return null;
        }

        if (grid is not { } target)
            return null;

        if (HasComp<MapComponent>(target))
            return Loc.GetString("bsp-error-map");

        if (HasComp<TradeStationComponent>(target))
            return Loc.GetString("bsp-error-trade-station");

        if (state.GridTileCount > _cfg.GetCVar(PersistenceCCVars.BluespaceParkingMaxTiles))
            return Loc.GetString("bsp-error-too-big", ("max", _cfg.GetCVar(PersistenceCCVars.BluespaceParkingMaxTiles)));

        return null;
    }

    private void UpdateUserInterface(Entity<BSPAnchorKeyComponent> ent)
    {
        _ui.SetUiState(ent.Owner, BSPAnchorKeyUiKey.Key, GetUIState(ent));
    }

    #endregion
}
