using Content.Shared._Persistence.Bank;
using Robust.Client.UserInterface;

namespace Content.Client._Persistence.Bank;

public sealed class BankATMBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private BankATMWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<BankATMWindow>();
        _window.OnWithdraw += amount => SendMessage(new BankWithdrawMessage(amount));
        _window.OnDeposit += () => SendMessage(new BankDepositMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is BankATMMenuInterfaceState atmState)
            _window?.UpdateState(atmState);
    }
}
