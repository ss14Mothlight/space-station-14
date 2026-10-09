using Content.Server._Persistence.JobNet;
using Content.Server.Hands.Systems;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// The ID printer, ported from SS14-Persistence: prints its user a new ID in their name, and deletes the ones it
/// printed for them before.
/// </summary>
public sealed partial class IdPrinterConsoleSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = null!;
    [Dependency] private FactionIdCardSystem _factionIdCard = null!;
    [Dependency] private HandsSystem _hands = null!;
    [Dependency] private JobNetSystem _jobNet = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IdPrinterConsoleComponent, IdPrinterPrintMessage>(OnPrint);
    }

    private void OnPrint(Entity<IdPrinterConsoleComponent> ent, ref IdPrinterPrintMessage args)
    {
        var user = args.Actor;
        var name = Name(user);

        _factionIdCard.ExpireAllIds(name);

        var id = Spawn(ent.Comp.IdCard, Transform(user).Coordinates);
        _factionIdCard.BuildID(id, name, _jobNet.GetWorkingFor(name));
        _hands.TryPickupAnyHand(user, id);

        _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):player} printed a replacement ID {ToPrettyString(id)} at {ToPrettyString(ent)}");
    }
}
