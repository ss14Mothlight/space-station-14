using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Persistence.Invoices;

/// <summary>
/// A printed invoice or payslip. Ported from SS14-Persistence.
/// An invoice asks whoever holds it to pay its cost to a faction or person, personally or out of a faction's money.
/// A payslip has already been paid for by a faction, and pays its cost out to whoever redeems it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class InvoiceComponent : Component
{
    [DataField]
    public int Cost;

    [DataField]
    public string Reason = string.Empty;

    /// <summary>
    /// The faction (by id) an invoice is paid to, or that paid for a payslip.
    /// </summary>
    [DataField]
    public int? TargetStation;

    /// <summary>
    /// The character an invoice is paid to.
    /// </summary>
    [DataField]
    public string? TargetPerson;

    /// <summary>
    /// The faction (by id) owning the grid the invoice was printed on, which takes its sales tax when it's paid.
    /// </summary>
    [DataField]
    public int TaxOwner;

    [DataField]
    public bool Payslip;

    [DataField]
    public bool Paid;

    [DataField]
    public string PaidBy = string.Empty;

    [DataField]
    public DateTime? PaidTime;

    [DataField]
    public SoundSpecifier PaySuccessSound = new SoundPathSpecifier("/Audio/Effects/kaching.ogg");

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");
}

[Serializable, NetSerializable]
public enum InvoiceVisuals : byte
{
    Paid,
}

/// <summary>
/// Prints invoices and payslips.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class InvoicePrinterConsoleComponent : Component
{
    public const string PrivilegedIdCardSlotId = "InvoicePrinter-privilegedId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    [DataField]
    public SoundSpecifier PrintSound = new SoundCollectionSpecifier("PrinterPrint");

    [DataField]
    public InvoicePrinterMode Mode = InvoicePrinterMode.PersonalInvoice;

    /// <summary>
    /// The faction (by id) that faction invoices are paid to, and that pays for payslips.
    /// </summary>
    [DataField]
    public int SelectedStation;
}

[Serializable, NetSerializable]
public enum InvoicePrinterMode : byte
{
    /// <summary>
    /// An invoice paid to the ID's holder.
    /// </summary>
    PersonalInvoice,

    /// <summary>
    /// An invoice paid to the selected faction.
    /// </summary>
    FactionInvoice,

    /// <summary>
    /// A payslip paid for by the selected faction.
    /// </summary>
    FactionPayslip,
}

[Serializable, NetSerializable]
public enum InvoicePrinterConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class InvoicePrinterConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public string? IdName;
    public string? HolderName;
    public InvoicePrinterMode Mode;
    public Dictionary<int, string> Factions = new();
    public int SelectedStation;

    /// <summary>
    /// The faction whose grid the printer is on, and its sales tax.
    /// </summary>
    public string? TaxingFaction;
    public int TaxRate;
}

[Serializable, NetSerializable]
public sealed class InvoicePrinterSetModeMessage(InvoicePrinterMode mode) : BoundUserInterfaceMessage
{
    public readonly InvoicePrinterMode Mode = mode;
}

[Serializable, NetSerializable]
public sealed class InvoicePrinterSelectStationMessage(int station) : BoundUserInterfaceMessage
{
    public readonly int Station = station;
}

[Serializable, NetSerializable]
public sealed class InvoicePrinterPrintMessage(string title, string reason, int cost) : BoundUserInterfaceMessage
{
    public readonly string Title = title;
    public readonly string Reason = reason;
    public readonly int Cost = cost;
}

[Serializable, NetSerializable]
public enum InvoiceUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class InvoiceBoundUserInterfaceState : BoundUserInterfaceState
{
    public int Cost;
    public string Reason = string.Empty;
    public string PayTo = string.Empty;
    public bool Payslip;
    public bool Paid;
    public string PaidBy = string.Empty;
    public DateTime? PaidTime;
    public int TaxRate;

    /// <summary>
    /// Factions the viewer can pay this from, or redeem this payslip into.
    /// </summary>
    public Dictionary<int, string> Factions = new();

    public int Credits;
}

/// <summary>
/// Pays an invoice (or redeems a payslip) from or to the given faction, or personally with 0.
/// </summary>
[Serializable, NetSerializable]
public sealed class InvoicePayMessage(int station) : BoundUserInterfaceMessage
{
    public readonly int Station = station;
}
