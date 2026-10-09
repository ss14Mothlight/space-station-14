using Content.Shared._Persistence.Radio;
using Robust.Client.UserInterface;

namespace Content.Client._Persistence.Radio;

public sealed class FactionHeadsetBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FactionHeadsetWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<FactionHeadsetWindow>();
        _window.OnTransmit += id => SendMessage(new FactionHeadsetTransmitSelect(id));
        _window.OnReceive += id => SendMessage(new FactionHeadsetReceiveSelect(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is FactionHeadsetBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}
