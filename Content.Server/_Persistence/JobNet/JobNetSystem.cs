using Content.Server._Persistence.Factions;
using Content.Server.Chat.Managers;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared._Persistence.JobNet;
using Content.Shared.Chat;
using Content.Shared._NullLink;
using Content.Shared.GameTicking;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Persistence.JobNet;

/// <summary>
/// The job network: clocking in to factions and getting paid for it. Ported from SS14-Persistence, without its
/// crime, precursor and world objective parts.
/// </summary>
public sealed partial class JobNetSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = null!;
    [Dependency] private IChatManager _chat = null!;
    [Dependency] private FactionIdCardSystem _factionIdCard = null!;
    [Dependency] private FactionSystem _faction = null!;
    [Dependency] private SharedSubdermalImplantSystem _implants = null!;
    [Dependency] private IPrototypeManager _proto = null!;
    [Dependency] private ISharedNullLinkPlayerResourcesManager _playerResources = null!;
    [Dependency] private UserInterfaceSystem _ui = null!;

    public static readonly EntProtoId JobNetImplant = "JobNetworkImplant";

    /// <summary>
    /// Wages are paid in, and network levels bought with, Starlight's credits (what the ATMs use).
    /// </summary>
    private const string CreditsResource = "credits";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<JobNetComponent, OpenJobNetImplantEvent>(OnImplantActivate);
        SubscribeLocalEvent<JobNetComponent, JobNetSelectMessage>(OnSelect);
        SubscribeLocalEvent<JobNetComponent, JobNetPurchaseMessage>(OnPurchase);
        SubscribeLocalEvent<FactionDataComponent, FactionJobNetDisabledEvent>(OnJobNetDisabled);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<JobNetComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.WorkingFor == 0)
                continue;

            comp.WorkedTime += TimeSpan.FromSeconds(frameTime);
            if (comp.WorkedTime < comp.PayPeriod)
                continue;

            comp.WorkedTime = TimeSpan.Zero;
            TryPay((uid, comp));
        }
    }

    #region Implant

    /// <summary>
    /// Everyone gets a JobNet implant, as everyone joins as a passenger on Persistence.
    /// </summary>
    private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
    {
        if (GetJobNet(ev.Mob) != null)
            return;

        _implants.AddImplant(ev.Mob, JobNetImplant);
    }

    private void OnImplantActivate(Entity<JobNetComponent> ent, ref OpenJobNetImplantEvent args)
    {
        if (!TryComp<ActorComponent>(args.Performer, out var actor))
            return;

        if (_ui.TryToggleUi(ent.Owner, JobNetUiKey.Key, actor.PlayerSession))
            UpdateUserInterface(args.Performer, ent);

        args.Handled = true;
    }

    /// <summary>
    /// The mob's JobNet implant, if it has one.
    /// </summary>
    public Entity<JobNetComponent>? GetJobNet(EntityUid mob)
    {
        if (!TryComp<ImplantedComponent>(mob, out var implanted))
            return null;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (TryComp<JobNetComponent>(implant, out var jobNet))
                return (implant, jobNet);
        }

        return null;
    }

    /// <summary>
    /// The faction the character is clocked in to, or 0.
    /// </summary>
    public int GetWorkingFor(string name)
    {
        var query = EntityQueryEnumerator<JobNetComponent, TransformComponent>();
        while (query.MoveNext(out var jobNet, out var xform))
        {
            if (jobNet.WorkingFor != 0 && Name(xform.ParentUid) == name)
                return jobNet.WorkingFor;
        }

        return 0;
    }

    /// <summary>
    /// The holder of a JobNet implant.
    /// </summary>
    private EntityUid GetHolder(EntityUid implant)
    {
        return Transform(implant).ParentUid;
    }

    #endregion

    #region Clocking in

    private void OnSelect(Entity<JobNetComponent> ent, ref JobNetSelectMessage args)
    {
        var name = Name(args.Actor);

        if (args.ID == 0 || _faction.GetStationByID(args.ID) is not { } station)
        {
            SetWorkingFor(ent, name, 0);
        }
        else
        {
            // Only clock in where you have an assignment, and the faction has JobNet on.
            if (!Comp<FactionDataComponent>(station).JobNetEnabled || !_faction.TryGetAssignment(name, station, out _))
                return;

            if (ent.Comp.LastWorkedFor != args.ID)
                ent.Comp.WorkedTime = TimeSpan.Zero;

            ent.Comp.LastWorkedFor = args.ID;
            SetWorkingFor(ent, name, args.ID);
        }

        UpdateUserInterface(args.Actor, ent);
    }

    private void SetWorkingFor(Entity<JobNetComponent> ent, string name, int factionId)
    {
        ent.Comp.WorkingFor = factionId;
        _factionIdCard.UpdateIDAssignment(name, factionId);
    }

    private void OnJobNetDisabled(Entity<FactionDataComponent> ent, ref FactionJobNetDisabledEvent args)
    {
        var query = EntityQueryEnumerator<JobNetComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.WorkingFor == ent.Comp.UID)
                SetWorkingFor((uid, comp), Name(GetHolder(uid)), 0);
        }
    }

    #endregion

    #region Pay

    /// <summary>
    /// Pays the holder their assignment's wage from the faction they're clocked in to.
    /// </summary>
    public void TryPay(Entity<JobNetComponent> ent)
    {
        if (_faction.GetStationByID(ent.Comp.WorkingFor) is not { } station)
        {
            ent.Comp.WorkingFor = 0;
            return;
        }

        var holder = GetHolder(ent);
        var name = Name(holder);
        if (!_faction.TryGetRecord(name, station, out var record)
            || !_faction.TryGetAssignment(name, station, out var assignment)
            || assignment.Wage <= 0
            || !TryComp<ActorComponent>(holder, out var actor))
        {
            return;
        }

        record.LastPaid = DateTime.Now;
        var factionName = Name(station);

        if (!_faction.TryAdjustFactionBalance(station, -assignment.Wage))
        {
            _audio.PlayEntity(ent.Comp.ErrorSound, holder, holder);
            Notify(actor.PlayerSession,
                Loc.GetString("jobnet-pay-failed", ("faction", factionName), ("wage", assignment.Wage)));
            return;
        }

        if (!_playerResources.TryUpdateResource(actor.PlayerSession, CreditsResource, assignment.Wage))
        {
            // Couldn't pay them after all, so the faction keeps its money.
            _faction.TryAdjustFactionBalance(station, assignment.Wage);
            _audio.PlayEntity(ent.Comp.ErrorSound, holder, holder);
            Notify(actor.PlayerSession,
                Loc.GetString("jobnet-pay-failed", ("faction", factionName), ("wage", assignment.Wage)));
            return;
        }

        _audio.PlayEntity(ent.Comp.PaySuccessSound, holder, holder);
        Notify(actor.PlayerSession,
            Loc.GetString("jobnet-paid",
                ("wage", assignment.Wage),
                ("assignment", assignment.Name),
                ("faction", factionName)));
    }

    private void Notify(ICommonSession session, string message)
    {
        _chat.ChatMessageToOne(ChatChannel.Notifications, message, message, EntityUid.Invalid, false, session.Channel);
    }

    #endregion

    #region Network levels

    private void OnPurchase(Entity<JobNetComponent> ent, ref JobNetPurchaseMessage args)
    {
        var name = Name(args.Actor);
        if (_faction.EnsureMetaRecord(name) is not { } record
            || !_proto.Resolve(record.Level, out var current)
            || current.Next is not { } nextId
            || !_proto.Resolve(nextId, out var next)
            || GetCredits(args.Actor) < next.Cost
            || !_playerResources.TryUpdateResource(args.Actor, CreditsResource, -next.Cost))
        {
            return;
        }

        record.Level = nextId;
        UpdateUserInterface(args.Actor, ent);
    }

    #endregion

    private int GetCredits(EntityUid user)
    {
        return _playerResources.TryGetResource(user, CreditsResource, out var credits) ? (int) credits.Value : 0;
    }

    #region UI

    public void UpdateUserInterface(EntityUid user, Entity<JobNetComponent> ent)
    {
        var name = Name(user);
        var state = new JobNetUpdateState
        {
            RemainingTime = ent.Comp.PayPeriod - ent.Comp.WorkedTime,
            Balance = GetCredits(user),
        };

        if (_faction.TryGetMetaRecord(name, out var meta))
            state.Level = meta!.Level;

        foreach (var station in _faction.GetStationsAvailableTo(name))
        {
            var data = Comp<FactionDataComponent>(station);
            if (data.JobNetEnabled && _faction.TryGetAssignment(name, station, out _))
                state.Stations[data.UID] = Name(station);

            if (data.UID != ent.Comp.WorkingFor || !_faction.TryGetAssignment(name, station, out var assignment))
                continue;

            state.SelectedStation = data.UID;
            state.AssignmentName = assignment.Name;
            state.Wage = assignment.Wage;
            state.IsOwner = data.IsOwner(name);
            state.SpendAuth = _faction.CanSpend(name, station);
            if (state.SpendAuth && _faction.TryGetRecord(name, station, out var record))
            {
                state.Spent = record.Spent;
                state.Spendable = assignment.SpendingLimit;
            }
        }

        _ui.SetUiState(ent.Owner, JobNetUiKey.Key, state);
    }

    #endregion
}
