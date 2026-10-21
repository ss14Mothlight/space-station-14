using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Server._Persistence.Bank; // Mothlight
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Starlight.Economy.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class SalaryAddCreditsCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private BankSystem _bank = default!; // Mothlight
    [Dependency] private IAdminLogManager _adminLog = default!;

    public override string Command => "salarychangecredits";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var amount))
        {
            shell.WriteError("Usage: salarychangecredits <player> <amount>");
            return;
        }

        if (!_players.TryGetSessionByUsername(args[0], out var player))
        {
            shell.WriteError($"Player not found: {args[0]}");
            return;
        }

        // Mothlight begin - change the bank balance of the character they're playing
        if (player.AttachedEntity is not { } character
            || !(amount >= 0 ? _bank.TryBankDeposit(character, amount) : _bank.TryBankWithdraw(character, -amount)))
        {
            shell.WriteError($"Could not update the bank balance of {player.Name}'s character.");
            return;
        }

        shell.WriteLine($"Changed the bank balance of {player.Name}'s character by {amount}.");
        // Mothlight end

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{shell.Player?.Name ?? "Console"} changed the bank balance of {player.Name}'s character by {amount}");
    }
}
