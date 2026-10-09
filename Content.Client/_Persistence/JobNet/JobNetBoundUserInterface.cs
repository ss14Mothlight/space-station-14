using Content.Shared._Persistence.JobNet;
using Robust.Client.UserInterface;

namespace Content.Client._Persistence.JobNet;

public sealed class JobNetBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private JobNetWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<JobNetWindow>();
        _window.OnSelect += id => SendMessage(new JobNetSelectMessage(id));
        _window.OnPurchase += () => SendMessage(new JobNetPurchaseMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is JobNetUpdateState cast)
            _window?.UpdateState(cast);
    }
}
