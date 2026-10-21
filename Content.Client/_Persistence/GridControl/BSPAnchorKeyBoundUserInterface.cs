using Content.Shared._Persistence.GridControl;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;

namespace Content.Client._Persistence.GridControl;

public sealed class BSPAnchorKeyBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private BSPAnchorKeyWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<BSPAnchorKeyWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.Owner = Owner;
        _window.PrivilegedIdButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(BSPAnchorKeyComponent.PrivilegedIdCardSlotId));
        _window.InitiateParkButton.OnPressed += _ => SendMessage(new BSPAnchorKeyStartPark());
        _window.CancelButton.OnPressed += _ => SendMessage(new BSPAnchorKeyCancel());
        _window.InitiateUnparkButton.OnPressed += _ => SendMessage(new BSPAnchorKeyStartUnpark());
        _window.ClearOwnershipButton.OnPressed += _ => SendMessage(new BSPAnchorKeyToggleClearOwnership());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is BSPAnchorKeyBoundUserInterfaceState keyState)
            _window?.UpdateState(keyState);
    }
}
