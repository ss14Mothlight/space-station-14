using Content.Server._Mothlight.Toolshed.Parsers;
using Content.Server._Starlight.Station.Systems;
using Content.Server._Starlight.Toolshed;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._Starlight.Commands;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;

namespace Content.Server._Mothlight.Administration.Commands;

/// <summary>
/// Late-joins a persistent character: loads their save if there is one, otherwise spawns them fresh and saves that.
/// Admins can pass <c>false</c> for <c>load</c> to discard the existing save and start over.
/// </summary>
[ToolshedCommand(Name="joingamepersist")]
[AnyCommand]
public sealed partial class JoinGameCommand : ToolshedCommand
{
    [Dependency] private IAdminManager _admin = null!;
    [Dependency] private IPrototypeManager _proto = null!;
    [Dependency] private IConfigurationManager _config = null!;
    [Dependency] private IServerPreferencesManager _prefs = null!;

    private GameTicker? _ticker;
    private StationJobsSystem? _jobs;
    private ContainerSpawnJobSlotSystem? _slots;

    [CommandImplementation]
    public void JoinGame(IInvocationContext ctx,
        [CommandArgument(typeof(CharacterProfileCompletionParser))]
        int slot,
        ProtoId<JobPrototype> job,
        [CommandArgument(typeof(EntityWithCompCompletionParser<StationDataComponent>))]
        EntityUid stationUid,
        bool load)
    {
        if (CommandHelpers.NoSession(ctx)) return;

        _ticker ??= GetSys<GameTicker>();
        _jobs ??= GetSys<StationJobsSystem>();
        _slots ??= GetSys<ContainerSpawnJobSlotSystem>();

        if (!load && !_admin.HasAdminFlag(ctx.Session!, AdminFlags.Fun))
        {
            CommandMarkup.Error(ctx, "Only admins can start a persistent character over.");
            return;
        }

        if (_ticker.RunLevel == GameRunLevel.PreRoundLobby)
        {
            CommandMarkup.Error(ctx, "Round has not started.");
            return;
        }

        if (_ticker.PlayerGameStatuses.TryGetValue(ctx.Session!.UserId, out var status) &&
            status == PlayerGameStatus.JoinedGame)
            if (ctx.Session.AttachedEntity is not null)
                if (!TryComp<GhostComponent>(ctx.Session.AttachedEntity.Value, out _))
                {
                    IoCManager.Resolve<ILogManager>().GetSawmill("toolshed]joingame").Warning(
                        $"Player {ctx.Session.Name} ({ctx.Session.UserId}) Tried to join game while already in-game.");
                    CommandMarkup.Warn(ctx, "Tried to join game while already in-game.");
                    return;
                }

        _slots.RefreshJobSlots();
        var jobProto = _proto.Index(job);
        if(!_jobs.TryGetJobSlot(stationUid, job, out var slots) || slots == 0)
        {
            CommandMarkup.Error(ctx, $"{jobProto.LocalizedName} has no available slots.");
            return;
        }

        if (!_prefs.GetPreferences(ctx.Session.UserId).TryGetHumanoidInSlot(slot, out var humanoid))
        {
            CommandMarkup.Error(ctx, "No profile in slot.");
            return;
        }

        if (_admin.IsAdmin(ctx.Session) && _config.GetCVar(CCVars.AdminDeadminOnJoin))
            _admin.DeAdmin(ctx.Session);

        if (load) _ticker.MakeJoinGamePersistentLoad(ctx.Session, humanoid, stationUid, job);
        else _ticker.MakeJoinGamePersistent(ctx.Session, humanoid, stationUid, job);
    }
}
