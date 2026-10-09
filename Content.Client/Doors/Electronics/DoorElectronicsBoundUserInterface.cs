using Content.Shared.Access;
using Content.Shared.Doors.Electronics;
using Content.Shared._Persistence.Factions.BUI; // Mothlight
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client.Doors.Electronics;

public sealed partial class DoorElectronicsBoundUserInterface : BoundUserInterface
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private DoorElectronicsConfigurationMenu? _window;

    public DoorElectronicsBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<DoorElectronicsConfigurationMenu>();
        _window.OnAccessChanged += UpdateConfiguration;
        // Mothlight begin
        _window.FactionAccess.OnToggleAccess += access => SendMessage(new FactionAccessReaderToggleMessage(access));
        _window.FactionAccess.OnPersonalMode += enabled => SendMessage(new FactionAccessReaderPersonalModeMessage(enabled));
        _window.FactionAccess.OnPersonalAdd += name => SendMessage(new FactionAccessReaderPersonalAddMessage(name));
        _window.FactionAccess.OnPersonalRemove += name => SendMessage(new FactionAccessReaderPersonalRemoveMessage(name));
        // Mothlight end
        // Starlight edit Start
        if (EntMan.TryGetComponent<MetaDataComponent>(Owner, out var meta))
            _window.Title = meta.EntityName;
        // Reset();
        // Starlight edit End
    }

    public override void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        base.OnProtoReload(args);

        if (!args.WasModified<AccessLevelPrototype>())
            return;

        // Starlight edit Start
        if (State is DoorElectronicsConfigurationState cast)
            _window?.UpdateState(cast.AccessList, cast.AccessGroups);
        // Starlight edit End
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        // Starlight Start
        base.UpdateState(state);
        if (state is not DoorElectronicsConfigurationState cast)
            return;
        // Starlight End
        _window?.UpdateState(cast.AccessList, cast.AccessGroups, cast.PressedAccessList); // Starlight edit
        _window?.FactionAccess.UpdateState(cast.Faction); // Mothlight
    }

    public void UpdateConfiguration(List<ProtoId<AccessLevelPrototype>> newAccessList)
    {
        SendMessage(new DoorElectronicsUpdateConfigurationMessage(newAccessList));
    }
}
