using Content.Shared._Persistence.Invoices;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;

namespace Content.Client._Persistence.Invoices;

public sealed class InvoicePrinterBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private InvoicePrinterWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<InvoicePrinterWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnMessage += SendMessage;
        _window.PrivilegedIdButton.OnPressed += _ =>
            SendMessage(new ItemSlotButtonPressedEvent(InvoicePrinterConsoleComponent.PrivilegedIdCardSlotId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is InvoicePrinterConsoleBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}

public sealed class InvoiceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private InvoiceWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<InvoiceWindow>();
        _window.OnPay += station => SendMessage(new InvoicePayMessage(station));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is InvoiceBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }
}
