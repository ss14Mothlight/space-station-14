using Content.Shared._Persistence.Factions.Components;
using Robust.Client.UserInterface;

namespace Content.Client._Persistence.Factions.UI;

public sealed class IdPrinterConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private IdPrinterConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<IdPrinterConsoleWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnPrint += () => SendMessage(new IdPrinterPrintMessage());
    }
}
