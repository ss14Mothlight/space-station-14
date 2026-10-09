using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._Persistence.Factions.BUI; // Mothlight
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using static Content.Shared.Access.Components.AccessOverriderComponent;

namespace Content.Client.Access.UI
{
    public sealed partial class AccessOverriderBoundUserInterface : BoundUserInterface
    {
        [Dependency] private IPrototypeManager _prototypeManager = default!;
        private readonly SharedAccessOverriderSystem _accessOverriderSystem = default!;

        private AccessOverriderWindow? _window;

        public AccessOverriderBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            _accessOverriderSystem = EntMan.System<SharedAccessOverriderSystem>();
        }

        protected override void Open()
        {
            base.Open();

            _window = this.CreateWindow<AccessOverriderWindow>();
            RefreshAccess();
            _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
            _window.OnSubmit += SubmitData;
            _window.OnGroupSelected += group => SendMessage(new AccessGroupSelectedMessage(group)); // Starlight-edit

            _window.PrivilegedIdButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(PrivilegedIdCardSlotId));
            // Mothlight begin
            _window.FactionAccess.OnToggleAccess += access => SendMessage(new FactionAccessReaderToggleMessage(access));
            _window.FactionAccess.OnPersonalMode += enabled => SendMessage(new FactionAccessReaderPersonalModeMessage(enabled));
            _window.FactionAccess.OnPersonalAdd += name => SendMessage(new FactionAccessReaderPersonalAddMessage(name));
            _window.FactionAccess.OnPersonalRemove += name => SendMessage(new FactionAccessReaderPersonalRemoveMessage(name));
            // Mothlight end
        }

        public override void OnProtoReload(PrototypesReloadedEventArgs args)
        {
            base.OnProtoReload(args);
            if (!args.WasModified<AccessLevelPrototype>() && !args.WasModified<AccessGroupPrototype>()) // Starlight-edit
                return;

            RefreshAccess();

            if (State != null)
                _window?.UpdateState(_prototypeManager, (AccessOverriderBoundUserInterfaceState) State);
        }

        private void RefreshAccess()
        {
            List<ProtoId<AccessLevelPrototype>> accessLevels;
            // Starlight-edit: Start
            List<ProtoId<AccessGroupPrototype>> accessGroups;
            ProtoId<AccessGroupPrototype>? currentGroup = null;
            // Starlight-edit: End

            if (EntMan.TryGetComponent<AccessOverriderComponent>(Owner, out var accessOverrider))
            {
                accessLevels = accessOverrider.AccessLevels;
                accessLevels.Sort();

                // Starlight-edit: Start
                accessGroups = accessOverrider.AccessGroups;
                accessGroups.Sort();
                // Starlight-edit: End

                currentGroup = accessOverrider.CurrentAccessGroup;
            }
            else
            {
                accessLevels = new List<ProtoId<AccessLevelPrototype>>();
                accessGroups = new List<ProtoId<AccessGroupPrototype>>(); // Starlight-edit
                _accessOverriderSystem.Log.Error($"No AccessOverrider component found for {EntMan.ToPrettyString(Owner)}!");
            }

            _window?.SetAccess(_prototypeManager, accessGroups, currentGroup, accessLevels); // Starlight-edit
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);
            var castState = (AccessOverriderBoundUserInterfaceState) state;
            _window?.UpdateState(_prototypeManager, castState);
            _window?.FactionAccess.UpdateState(castState.Faction); // Mothlight
        }

        public void SubmitData(List<ProtoId<AccessLevelPrototype>> newAccessList)
        {
            SendMessage(new WriteToTargetAccessReaderIdMessage(newAccessList));
        }
    }
}
