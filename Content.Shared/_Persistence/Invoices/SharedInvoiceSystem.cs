using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Persistence.Invoices;

/// <summary>
/// Sets up the invoice printer's ID slot.
/// </summary>
public sealed partial class SharedInvoiceSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InvoicePrinterConsoleComponent, ComponentInit>((uid, comp, _) =>
            _itemSlots.AddItemSlot(uid, InvoicePrinterConsoleComponent.PrivilegedIdCardSlotId, comp.PrivilegedIdSlot));
        SubscribeLocalEvent<InvoicePrinterConsoleComponent, ComponentRemove>((uid, comp, _) =>
            _itemSlots.RemoveItemSlot(uid, comp.PrivilegedIdSlot));
    }
}
