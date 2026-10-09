using System.Linq;
using Content.Server._Persistence.Factions;
using Content.Server.Cargo.Components;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared._Persistence.GridControl;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Persistence.GridControl;

/// <summary>
/// The faction grid tools, ported from SS14-Persistence:
/// <list type="bullet">
/// <item>the station creator founds a faction,</item>
/// <item>the grid configurator claims, unclaims and renames grids, for a faction or for one person,</item>
/// <item>the station tagger ties a machine or door to a faction wherever it is,</item>
/// <item>and the grid control console stops its grid from being unclaimed.</item>
/// </list>
/// </summary>
public sealed partial class FactionGridSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _accessReader = null!;
    [Dependency] private ISharedAdminLogManager _adminLogger = null!;
    [Dependency] private SharedDoAfterSystem _doAfter = null!;
    [Dependency] private FactionSystem _faction = null!;
    [Dependency] private SharedInteractionSystem _interaction = null!;
    [Dependency] private SharedIdCardSystem _idCard = null!;
    [Dependency] private ItemSlotsSystem _itemSlots = null!;
    [Dependency] private SharedMapSystem _map = null!;
    [Dependency] private MetaDataSystem _metaData = null!;
    [Dependency] private PopupSystem _popup = null!;
    [Dependency] private IPrototypeManager _proto = null!;
    [Dependency] private StationSystem _station = null!;
    [Dependency] private SharedTransformSystem _transform = null!;
    [Dependency] private UserInterfaceSystem _ui = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridConfigComponent, EntInsertedIntoContainerMessage>((uid, comp, _) => UpdateGridConfig((uid, comp)));
        SubscribeLocalEvent<GridConfigComponent, EntRemovedFromContainerMessage>(OnGridConfigIdRemoved);
        SubscribeLocalEvent<StationCreatorComponent, EntInsertedIntoContainerMessage>((uid, comp, _) => UpdateStationCreator((uid, comp)));
        SubscribeLocalEvent<StationCreatorComponent, EntRemovedFromContainerMessage>((uid, comp, _) => UpdateStationCreator((uid, comp)));
        SubscribeLocalEvent<StationTaggerComponent, EntInsertedIntoContainerMessage>((uid, comp, _) => UpdateStationTagger((uid, comp)));
        SubscribeLocalEvent<StationTaggerComponent, EntRemovedFromContainerMessage>(OnTaggerIdRemoved);
        SubscribeLocalEvent<StationTaggerComponent, AfterInteractEvent>(OnTaggerAfterInteract);
        SubscribeLocalEvent<StationTaggerComponent, StationTaggerDoAfterEvent>(OnTaggerDoAfter);

        Subs.BuiEvents<GridConfigComponent>(GridConfigUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>((uid, comp, _) => UpdateGridConfig((uid, comp)));
                subs.Event<GridConfigChangeName>(OnChangeName);
                subs.Event<GridConfigTargetSelect>(OnGridConfigTargetSelect);
                subs.Event<GridConfigChangeMode>(OnChangeMode);
                subs.Event<GridConfigConnect>(OnConnect);
                subs.Event<GridConfigDisconnect>(OnDisconnect);
            });

        Subs.BuiEvents<StationCreatorComponent>(StationCreatorUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>((uid, comp, _) => UpdateStationCreator((uid, comp)));
                subs.Event<StationCreatorFinish>(OnStationCreate);
            });

        Subs.BuiEvents<StationTaggerComponent>(StationTaggerUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>((uid, comp, _) => UpdateStationTagger((uid, comp)));
                subs.Event<StationTaggerTargetSelect>(OnTaggerTargetSelect);
                subs.Event<StationTaggerLink>(OnLink);
                subs.Event<StationTaggerUnlink>(OnUnlink);
            });

        Subs.BuiEvents<GridControlConsoleComponent>(GridControlConsoleUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>((uid, comp, _) => UpdateGridControl((uid, comp)));
                subs.Event<GridControlSetActive>(OnGridControlSetActive);
            });
    }

    #region Helpers

    private string? GetIdName(ItemSlot slot)
    {
        return slot.Item is { } id ? _faction.GetIdName(id) : null;
    }

    private string? GetIdEntityName(ItemSlot slot)
    {
        return slot.Item is { } id ? Name(id) : null;
    }

    /// <summary>
    /// The factions the character may claim grids or tag things for.
    /// </summary>
    private Dictionary<int, string> GetClaimableFactions(string? name)
    {
        var factions = new Dictionary<int, string>();
        if (name == null)
            return factions;

        foreach (var station in _faction.GetFactions())
        {
            if (_faction.GetGridAccess(null, name, station, null, ignoreGrid: true))
                factions[_faction.GetStationID(station)] = Name(station);
        }

        return factions;
    }

    private int GetTileCount(EntityUid grid)
    {
        return TryComp<MapGridComponent>(grid, out var gridComp) ? _map.GetAllTiles(grid, gridComp).Count() : 0;
    }

    /// <summary>
    /// Whether an active grid control console is keeping the grid claimed.
    /// </summary>
    public bool IsGridControlled(EntityUid grid)
    {
        var query = EntityQueryEnumerator<GridControlConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var console, out var xform))
        {
            if (console.Active && xform.GridUid == grid)
                return true;
        }

        return false;
    }

    #endregion

    #region Grid configurator

    private void OnGridConfigIdRemoved(Entity<GridConfigComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        ent.Comp.ConnectedStation = null;
        UpdateGridConfig(ent);
    }

    private void OnChangeMode(Entity<GridConfigComponent> ent, ref GridConfigChangeMode args)
    {
        ent.Comp.PersonalMode = !ent.Comp.PersonalMode;
        UpdateGridConfig(ent);
    }

    private void OnGridConfigTargetSelect(Entity<GridConfigComponent> ent, ref GridConfigTargetSelect args)
    {
        ent.Comp.ConnectedStation = args.Target == 0 ? null : args.Target;
        UpdateGridConfig(ent);
    }

    /// <summary>
    /// Whether the ID in the configurator may change the grid it's on: its owner, someone in its owning faction
    /// who can claim, or anyone at all if it's unclaimed.
    /// </summary>
    private bool CanModifyGrid(Entity<GridConfigComponent> ent, out string name)
    {
        name = GetIdName(ent.Comp.PrivilegedIdSlot) ?? string.Empty;
        var grid = _transform.GetGrid(ent.Owner);
        _faction.GetOwning(ent, out var owningStation, out var owningPerson);
        return _faction.GetGridAccess(grid, name, owningStation, owningPerson);
    }

    /// <summary>
    /// Why the grid the configurator is on can't be claimed for its current target, or null if it can.
    /// </summary>
    private string? GetClaimError(Entity<GridConfigComponent> ent, string name)
    {
        if (_transform.GetGrid(ent.Owner) is not { } grid)
            return Loc.GetString("grid-config-no-grid");

        var tiles = GetTileCount(grid);
        var isTradeStation = HasComp<TradeStationComponent>(grid);

        if (ent.Comp.PersonalMode)
        {
            if (isTradeStation)
                return Loc.GetString("grid-config-trade-station-personal");

            if (!_faction.TryGetMetaRecord(name, out var record)
                || !_proto.Resolve(record!.Level, out var networkLevel)
                || networkLevel.TileLimit < 1)
            {
                return Loc.GetString("grid-config-no-personal-claims");
            }

            if (tiles + _faction.GetPersonalTileCount(name) > networkLevel.TileLimit)
                return Loc.GetString("grid-config-tile-limit");

            return null;
        }

        if (ent.Comp.ConnectedStation is not { } target || _faction.GetStationByID(target) is not { } station)
            return Loc.GetString("grid-config-no-target");

        if (!_faction.GetGridAccess(grid, name, station, null))
            return Loc.GetString("grid-config-not-authorized-target");

        if (!_proto.Resolve(Comp<FactionDataComponent>(station).Level, out var level))
            return null;

        if (isTradeStation)
        {
            if (!level.TradestationClaim)
                return Loc.GetString("grid-config-trade-station-level");

            if (_faction.GetStationTradeStation(station) is { } current && current != grid)
                return Loc.GetString("grid-config-trade-station-owned");
        }

        if (tiles + _faction.GetStationTileCount(station) > level.TileLimit)
            return Loc.GetString("grid-config-tile-limit");

        return null;
    }

    private void OnChangeName(Entity<GridConfigComponent> ent, ref GridConfigChangeName args)
    {
        var name = args.Name.Trim();
        if (name.Length == 0
            || name.Length > FactionSystem.MaxNameLength * 2
            || !CanModifyGrid(ent, out _)
            || _transform.GetGrid(ent.Owner) is not { } grid)
        {
            return;
        }

        _metaData.SetEntityName(grid, name);
        UpdateGridConfig(ent);
    }

    private void OnConnect(Entity<GridConfigComponent> ent, ref GridConfigConnect args)
    {
        if (!CanModifyGrid(ent, out var name)
            || GetClaimError(ent, name) is { } error
            || _transform.GetGrid(ent.Owner) is not { } grid)
        {
            return;
        }

        // Only unclaimed grids can be claimed.
        _faction.GetOwning(grid, out var owningStation, out var owningPerson);
        if (owningStation != null || owningPerson != null)
            return;

        if (ent.Comp.PersonalMode)
        {
            _faction.AddGridToPerson(name, grid);
            _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.Actor):player} claimed {ToPrettyString(grid)} for {name}");
        }
        else if (ent.Comp.ConnectedStation is { } target && _faction.GetStationByID(target) is { } station)
        {
            _station.AddGridToStation(station, grid);
            _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.Actor):player} claimed {ToPrettyString(grid)} for {ToPrettyString(station)}");
        }

        UpdateGridConfig(ent);
    }

    private void OnDisconnect(Entity<GridConfigComponent> ent, ref GridConfigDisconnect args)
    {
        if (!CanModifyGrid(ent, out _)
            || _transform.GetGrid(ent.Owner) is not { } grid
            || IsGridControlled(grid))
        {
            return;
        }

        if (_faction.GetOwningFaction(grid) is { } station)
        {
            _station.RemoveGridFromStation(station, grid);
            _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.Actor):player} unclaimed {ToPrettyString(grid)} from {ToPrettyString(station)}");
        }
        else if (_faction.GetOwningPerson(grid) is { } person)
        {
            _faction.RemoveGridFromPerson(grid);
            _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.Actor):player} unclaimed {ToPrettyString(grid)} from {person}");
        }

        UpdateGridConfig(ent);
    }

    private void UpdateGridConfig(Entity<GridConfigComponent> ent)
    {
        var name = GetIdName(ent.Comp.PrivilegedIdSlot);
        var grid = _transform.GetGrid(ent.Owner);
        _faction.GetOwning(ent, out var owningStation, out var owningPerson);

        var state = new GridConfigBoundUserInterfaceState
        {
            IdName = GetIdEntityName(ent.Comp.PrivilegedIdSlot),
            PersonalMode = ent.Comp.PersonalMode,
            OwnerName = owningPerson,
            GridName = grid is { } g ? Name(g) : null,
            GridTileCount = grid is { } tilesGrid ? GetTileCount(tilesGrid) : 0,
            IsControlled = grid is { } controlledGrid && IsGridControlled(controlledGrid),
            IsAuth = name != null && _faction.GetGridAccess(grid, name, owningStation, owningPerson),
            PossibleStations = GetClaimableFactions(name),
            TargetStation = ent.Comp.ConnectedStation,
        };

        if (ent.Comp.PersonalMode)
        {
            state.TargetName = name;
            if (name != null)
            {
                state.CurrentTileCount = _faction.GetPersonalTileCount(name);
                if (_faction.TryGetMetaRecord(name, out var record) && _proto.Resolve(record!.Level, out var networkLevel))
                    state.TileLimit = networkLevel.TileLimit;
            }
        }
        else if (ent.Comp.ConnectedStation is { } target && _faction.GetStationByID(target) is { } station)
        {
            state.TargetName = Name(station);
            state.CurrentTileCount = _faction.GetStationTileCount(station);
            if (_proto.Resolve(Comp<FactionDataComponent>(station).Level, out var level))
                state.TileLimit = level.TileLimit;
        }

        if (name != null && owningStation == null && owningPerson == null)
            state.ErrorMessage = GetClaimError(ent, name);

        _ui.SetUiState(ent.Owner, GridConfigUiKey.Key, state);
    }

    #endregion

    #region Station creator

    private void OnStationCreate(Entity<StationCreatorComponent> ent, ref StationCreatorFinish args)
    {
        var stationName = args.StationName.Trim();
        if (GetIdName(ent.Comp.PrivilegedIdSlot) is not { } owner
            || stationName.Length == 0
            || stationName.Length > FactionSystem.MaxNameLength)
        {
            return;
        }

        var station = _faction.CreateFaction(stationName, owner);
        _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.Actor):player} founded faction {ToPrettyString(station)}, owned by {owner}");

        if (ent.Comp.PrivilegedIdSlot.Item is { } id)
        {
            _popup.PopupEntity(Loc.GetString("station-creator-created", ("name", stationName)), id, args.Actor);
            _itemSlots.TryEjectToHands(ent, ent.Comp.PrivilegedIdSlot, args.Actor);
        }

        QueueDel(ent);
    }

    private void UpdateStationCreator(Entity<StationCreatorComponent> ent)
    {
        var state = new StationCreatorBoundUserInterfaceState
        {
            IdName = GetIdEntityName(ent.Comp.PrivilegedIdSlot),
            RealName = GetIdName(ent.Comp.PrivilegedIdSlot),
        };
        _ui.SetUiState(ent.Owner, StationCreatorUiKey.Key, state);
    }

    #endregion

    #region Station tagger

    private void OnTaggerIdRemoved(Entity<StationTaggerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        ent.Comp.ConnectedStation = null;
        UpdateStationTagger(ent);
    }

    private void OnTaggerTargetSelect(Entity<StationTaggerComponent> ent, ref StationTaggerTargetSelect args)
    {
        ent.Comp.ConnectedStation = args.Target == 0 ? null : args.Target;
        UpdateStationTagger(ent);
    }

    private void OnTaggerAfterInteract(Entity<StationTaggerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled
            || !args.CanReach
            || args.Target is not { } target
            || !HasComp<AccessReaderComponent>(target)
            || !_interaction.InRangeUnobstructed(args.User, target))
        {
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.DoAfter, new StationTaggerDoAfterEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
        args.Handled = true;
    }

    private void OnTaggerDoAfter(Entity<StationTaggerComponent> ent, ref StationTaggerDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        ent.Comp.Target = target;
        _ui.OpenUi(ent.Owner, StationTaggerUiKey.Key, args.User);
        UpdateStationTagger(ent);
        args.Handled = true;
    }

    /// <summary>
    /// The entity the tag actually goes on: the target's main access reader.
    /// </summary>
    private EntityUid? GetTagTarget(Entity<StationTaggerComponent> ent)
    {
        if (ent.Comp.Target is not { } target
            || TerminatingOrDeleted(target)
            || !_accessReader.GetMainAccessReader(target, out var reader))
        {
            return null;
        }

        return reader.Value.Owner;
    }

    /// <summary>
    /// Tagging something requires passing its access reader, and being allowed to claim for the faction it's being
    /// tagged for (or, when removing a tag, the faction it's tagged for).
    /// </summary>
    private bool CanTag(Entity<StationTaggerComponent> ent, EntityUid reader, int factionId)
    {
        if (ent.Comp.PrivilegedIdSlot.Item is not { } id
            || GetIdName(ent.Comp.PrivilegedIdSlot) is not { } name
            || !_accessReader.IsAllowed(id, reader)
            || _faction.GetStationByID(factionId) is not { } station)
        {
            return false;
        }

        return _faction.GetGridAccess(null, name, station, null, ignoreGrid: true);
    }

    private void OnLink(Entity<StationTaggerComponent> ent, ref StationTaggerLink args)
    {
        if (GetTagTarget(ent) is not { } reader
            || ent.Comp.ConnectedStation is not { } factionId
            || HasComp<FactionTagComponent>(reader)
            || !CanTag(ent, reader, factionId))
        {
            return;
        }

        var tag = EnsureComp<FactionTagComponent>(reader);
        tag.FactionId = factionId;
        Dirty(reader, tag);
        _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.Actor):player} tagged {ToPrettyString(reader)} for faction {factionId}");
        UpdateStationTagger(ent);
    }

    private void OnUnlink(Entity<StationTaggerComponent> ent, ref StationTaggerUnlink args)
    {
        if (GetTagTarget(ent) is not { } reader
            || !TryComp<FactionTagComponent>(reader, out var tag)
            || !CanTag(ent, reader, tag.FactionId))
        {
            return;
        }

        RemComp<FactionTagComponent>(reader);
        _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.Actor):player} removed the faction tag from {ToPrettyString(reader)}");
        UpdateStationTagger(ent);
    }

    private void UpdateStationTagger(Entity<StationTaggerComponent> ent)
    {
        var name = GetIdName(ent.Comp.PrivilegedIdSlot);
        var state = new StationTaggerBoundUserInterfaceState
        {
            IdName = GetIdEntityName(ent.Comp.PrivilegedIdSlot),
            PossibleStations = GetClaimableFactions(name),
            TargetStation = ent.Comp.ConnectedStation,
        };

        if (GetTagTarget(ent) is { } reader)
        {
            state.TargetName = Name(ent.Comp.Target!.Value);
            if (TryComp<FactionTagComponent>(reader, out var tag) && _faction.GetStationByID(tag.FactionId) is { } tagged)
                state.TaggedFaction = Name(tagged);

            state.IsAuthorized = ent.Comp.PrivilegedIdSlot.Item is { } id && _accessReader.IsAllowed(id, reader);
        }

        _ui.SetUiState(ent.Owner, StationTaggerUiKey.Key, state);
    }

    #endregion

    #region Grid control console

    private void OnGridControlSetActive(Entity<GridControlConsoleComponent> ent, ref GridControlSetActive args)
    {
        // Mothlight: also only whoever could unclaim the grid by hand gets to stop the console protecting it.
        _faction.GetOwning(ent, out var owningStation, out var owningPerson);
        var name = _idCard.TryFindIdCard(args.Actor, out var id) ? id.Comp.FullName : null;
        if (!_accessReader.IsAllowed(args.Actor, ent)
            || name == null
            || !_faction.GetGridAccess(_transform.GetGrid(ent.Owner), name, owningStation, owningPerson))
        {
            return;
        }

        ent.Comp.Active = args.Active;
        UpdateGridControl(ent);
    }

    private void UpdateGridControl(Entity<GridControlConsoleComponent> ent)
    {
        _ui.SetUiState(ent.Owner, GridControlConsoleUiKey.Key, new GridControlConsoleBoundUserInterfaceState(ent.Comp.Active));
    }

    #endregion
}
