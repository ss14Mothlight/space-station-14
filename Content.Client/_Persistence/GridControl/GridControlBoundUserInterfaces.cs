using Content.Shared._Persistence.GridControl;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;

namespace Content.Client._Persistence.GridControl;

public sealed class GridConfigBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private GridConfigWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<GridConfigWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnMessage += SendMessage;
        _window.PrivilegedIdButton.OnPressed += _ =>
            SendMessage(new ItemSlotButtonPressedEvent(GridConfigComponent.PrivilegedIdCardSlotId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is GridConfigBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}

public sealed class StationCreatorBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private StationCreatorWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<StationCreatorWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnFinish += name => SendMessage(new StationCreatorFinish(name));
        _window.PrivilegedIdButton.OnPressed += _ =>
            SendMessage(new ItemSlotButtonPressedEvent(StationCreatorComponent.PrivilegedIdCardSlotId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is StationCreatorBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}

public sealed class StationTaggerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private StationTaggerWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<StationTaggerWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnMessage += SendMessage;
        _window.PrivilegedIdButton.OnPressed += _ =>
            SendMessage(new ItemSlotButtonPressedEvent(StationTaggerComponent.PrivilegedIdCardSlotId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is StationTaggerBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}

public sealed class GridControlConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private GridControlConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<GridControlConsoleWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnSetActive += active => SendMessage(new GridControlSetActive(active));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is GridControlConsoleBoundUserInterfaceState cast)
            _window?.UpdateState(cast.Active);
    }
}
