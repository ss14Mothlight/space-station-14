using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.Doors.Electronics;
using Content.Shared._Persistence.Factions;
using Content.Shared._Persistence.Factions.BUI;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Electronics;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// Handles the faction section of the door electronics and access overrider UIs.
/// Faction accesses can only be added or removed by the faction's owners, or by someone whose assignment grants
/// that access. Personal mode can be set up by anyone who can configure the reader.
/// </summary>
public sealed partial class FactionAccessReaderUiSystem : EntitySystem
{
    [Dependency] private AccessOverriderSystem _overrider = null!;
    [Dependency] private AccessReaderSystem _accessReader = null!;
    [Dependency] private DoorElectronicsSystem _doorElectronics = null!;
    [Dependency] private FactionAccessSystem _factionAccess = null!;
    [Dependency] private FactionSystem _faction = null!;
    [Dependency] private IdCardSystem _idCard = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoorElectronicsComponent, FactionAccessReaderToggleMessage>(OnElectronicsToggle);
        SubscribeLocalEvent<DoorElectronicsComponent, FactionAccessReaderPersonalModeMessage>(OnElectronicsMode);
        SubscribeLocalEvent<DoorElectronicsComponent, FactionAccessReaderPersonalAddMessage>(OnElectronicsAdd);
        SubscribeLocalEvent<DoorElectronicsComponent, FactionAccessReaderPersonalRemoveMessage>(OnElectronicsRemove);

        SubscribeLocalEvent<AccessOverriderComponent, FactionAccessReaderToggleMessage>(OnOverriderToggle);
        SubscribeLocalEvent<AccessOverriderComponent, FactionAccessReaderPersonalModeMessage>(OnOverriderMode);
        SubscribeLocalEvent<AccessOverriderComponent, FactionAccessReaderPersonalAddMessage>(OnOverriderAdd);
        SubscribeLocalEvent<AccessOverriderComponent, FactionAccessReaderPersonalRemoveMessage>(OnOverriderRemove);
    }

    /// <summary>
    /// Builds the faction section of a reader's configuration UI.
    /// </summary>
    /// <param name="reader">The entity whose access reader is being configured.</param>
    /// <param name="editorName">Character name of whoever is configuring it, from their ID.</param>
    public FactionAccessReaderState BuildState(EntityUid reader, string? editorName)
    {
        var state = new FactionAccessReaderState();

        if (TryComp<FactionAccessReaderComponent>(reader, out var factionReader))
        {
            state.RequiredAccesses = factionReader.AccessNames.ToList();
            state.PersonalAccessMode = factionReader.PersonalAccessMode;
            state.PersonalAccessNames = factionReader.PersonalAccessNames.ToList();
        }

        if (_faction.GetOwningFaction(reader) is not { } station)
            return state;

        state.FactionName = Name(station);
        if (TryComp<CrewAccessesComponent>(station, out var accesses))
            state.FactionAccesses = accesses.CrewAccesses.OrderBy(a => a).ToList();

        if (editorName == null)
            return state;

        state.EditableAccesses = _faction.IsOwner(editorName, station)
            ? state.FactionAccesses.ToList()
            : _faction.GetGrantedAccesses(editorName, station).Where(state.FactionAccesses.Contains).ToList();

        return state;
    }

    /// <summary>
    /// Character name on the ID the entity is carrying, if any.
    /// </summary>
    public string? GetActorName(EntityUid actor)
    {
        return _idCard.TryFindIdCard(actor, out var id) ? id.Comp.FullName : null;
    }

    private bool TryToggle(EntityUid reader, string? editorName, string access)
    {
        if (!BuildState(reader, editorName).EditableAccesses.Contains(access))
            return false;

        _factionAccess.ToggleAccess(reader, access);
        return true;
    }

    #region Door electronics

    private void OnElectronicsToggle(Entity<DoorElectronicsComponent> ent, ref FactionAccessReaderToggleMessage args)
    {
        if (TryToggle(ent, GetActorName(args.Actor), args.Access))
            _doorElectronics.UpdateUserInterface(ent, ent.Comp);
    }

    private void OnElectronicsMode(Entity<DoorElectronicsComponent> ent, ref FactionAccessReaderPersonalModeMessage args)
    {
        _factionAccess.SetPersonalMode(ent.Owner, args.Enabled);
        _doorElectronics.UpdateUserInterface(ent, ent.Comp);
    }

    private void OnElectronicsAdd(Entity<DoorElectronicsComponent> ent, ref FactionAccessReaderPersonalAddMessage args)
    {
        _factionAccess.AddPersonalAccess(ent.Owner, args.Name.Trim());
        _doorElectronics.UpdateUserInterface(ent, ent.Comp);
    }

    private void OnElectronicsRemove(Entity<DoorElectronicsComponent> ent, ref FactionAccessReaderPersonalRemoveMessage args)
    {
        _factionAccess.RemovePersonalAccess(ent.Owner, args.Name);
        _doorElectronics.UpdateUserInterface(ent, ent.Comp);
    }

    #endregion

    #region Access overrider

    /// <summary>
    /// The access reader an access overrider is configuring, if it's allowed to.
    /// </summary>
    private bool TryGetOverriderTarget(Entity<AccessOverriderComponent> ent, out EntityUid reader, out string? editorName)
    {
        reader = default;
        editorName = null;

        if (!_overrider.IsPrivilegedIdAuthorized(ent)
            || ent.Comp.TargetAccessReaderId is not { Valid: true } target
            || !_accessReader.GetMainAccessReader(target, out var mainReader))
        {
            return false;
        }

        reader = mainReader.Value.Owner;
        if (ent.Comp.PrivilegedIdSlot.Item is { } id)
            editorName = _faction.GetIdName(id);

        return true;
    }

    private void OnOverriderToggle(Entity<AccessOverriderComponent> ent, ref FactionAccessReaderToggleMessage args)
    {
        if (TryGetOverriderTarget(ent, out var reader, out var editor) && TryToggle(reader, editor, args.Access))
            _overrider.RefreshUserInterface(ent);
    }

    private void OnOverriderMode(Entity<AccessOverriderComponent> ent, ref FactionAccessReaderPersonalModeMessage args)
    {
        if (!TryGetOverriderTarget(ent, out var reader, out _))
            return;

        _factionAccess.SetPersonalMode(reader, args.Enabled);
        _overrider.RefreshUserInterface(ent);
    }

    private void OnOverriderAdd(Entity<AccessOverriderComponent> ent, ref FactionAccessReaderPersonalAddMessage args)
    {
        if (!TryGetOverriderTarget(ent, out var reader, out _))
            return;

        _factionAccess.AddPersonalAccess(reader, args.Name.Trim());
        _overrider.RefreshUserInterface(ent);
    }

    private void OnOverriderRemove(Entity<AccessOverriderComponent> ent, ref FactionAccessReaderPersonalRemoveMessage args)
    {
        if (!TryGetOverriderTarget(ent, out var reader, out _))
            return;

        _factionAccess.RemovePersonalAccess(reader, args.Name);
        _overrider.RefreshUserInterface(ent);
    }

    #endregion
}
