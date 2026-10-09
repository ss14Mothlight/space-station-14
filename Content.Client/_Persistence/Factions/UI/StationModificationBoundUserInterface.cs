using Content.Shared._Persistence.Factions.BUI;
using Robust.Client.UserInterface;

namespace Content.Client._Persistence.Factions.UI;

public sealed class StationModificationBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private StationModificationMenu? _menu;

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<StationModificationMenu>();
        _menu.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _menu.OnMessage += SendMessage;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is StationModificationInterfaceState cast)
            _menu?.UpdateState(cast);
    }
}
