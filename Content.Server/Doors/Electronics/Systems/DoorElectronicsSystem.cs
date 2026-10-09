using System.Linq;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Electronics;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared.Emag.Systems; // Starlight
using Content.Server._Persistence.Factions; // Mothlight

namespace Content.Server.Doors.Electronics;

public sealed partial class DoorElectronicsSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private EmagSystem _emag = default!; // Starlight
    [Dependency] private FactionAccessReaderUiSystem _factionUi = null!; // Mothlight

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoorElectronicsComponent, DoorElectronicsUpdateConfigurationMessage>(OnChangeConfiguration);
        SubscribeLocalEvent<DoorElectronicsComponent, AccessReaderConfigurationChangedEvent>(OnAccessReaderChanged);
        SubscribeLocalEvent<DoorElectronicsComponent, BoundUIOpenedEvent>(OnBoundUIOpened);
        SubscribeLocalEvent<DoorElectronicsComponent, GotEmaggedEvent>(OnGotEmagged); // Starlight
    }

    public void UpdateUserInterface(EntityUid uid, DoorElectronicsComponent component)
    {
        // var accesses = new List<ProtoId<AccessLevelPrototype>>(); // Starlight edit

        // Starlight edit Start
        var protoMan = IoCManager.Resolve<IPrototypeManager>();
        var allLevels = new HashSet<ProtoId<AccessLevelPrototype>>();
        foreach (var group in component.AccessGroups)
        {
            if (protoMan.TryIndex(group, out AccessGroupPrototype? groupProto))
                allLevels.UnionWith(groupProto.Tags);
        }
        var possibleAccesses = allLevels.OrderBy(x => x).ToList();

        var pressedAccesses = new List<ProtoId<AccessLevelPrototype>>();
        if (TryComp<AccessReaderComponent>(uid, out var accessReader))
        {
            foreach (var accessList in accessReader.AccessLists)
                pressedAccesses.AddRange(accessList);
        }
        var state = new DoorElectronicsConfigurationState(possibleAccesses, component.AccessGroups, pressedAccesses);
        // Starlight edit End
        // Mothlight begin - faction accesses, editable by whoever has it open
        string? editor = null;
        foreach (var actor in _uiSystem.GetActors(uid, DoorElectronicsConfigurationUiKey.Key))
        {
            editor = _factionUi.GetActorName(actor);
            break;
        }

        state.Faction = _factionUi.BuildState(uid, editor);
        // Mothlight end
        _uiSystem.SetUiState(uid, DoorElectronicsConfigurationUiKey.Key, state); // Starlight edit
    }

    private void OnChangeConfiguration(
        EntityUid uid,
        DoorElectronicsComponent component,
        DoorElectronicsUpdateConfigurationMessage args)
    {
        var accessReader = EnsureComp<AccessReaderComponent>(uid);
        _accessReader.TrySetAccesses((uid, accessReader), args.AccessList);
    }

    private void OnAccessReaderChanged(
        EntityUid uid,
        DoorElectronicsComponent component,
        AccessReaderConfigurationChangedEvent args)
    {
        UpdateUserInterface(uid, component);
    }

    private void OnBoundUIOpened(
        EntityUid uid,
        DoorElectronicsComponent component,
        BoundUIOpenedEvent args)
    {
        UpdateUserInterface(uid, component);
    }

    // Starlight begin
    private void OnGotEmagged(EntityUid uid, DoorElectronicsComponent comp, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        if (_emag.CheckFlag(uid, EmagType.Interaction, args.EmagComponent))
            return;

        if (args.EmagComponent is null) return;

        var addedGroups = false;
        foreach (var group in args.EmagComponent.AccessGroups.Where(group => !comp.AccessGroups.Contains(group)))
        {
            comp.AccessGroups.Add(group);
            addedGroups = true;
        }

        if (!addedGroups) return;
        args.Handled = true;
    }
    // Starlight end
}
