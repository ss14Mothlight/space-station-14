using System.Linq;
using Content.Server._Persistence.Factions;
using Content.Server.Access.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared._Persistence.Radio;
using Content.Shared._Starlight.Clothing;
using Content.Shared._Starlight.Language;
using Content.Shared._Starlight.Speech;
using Content.Shared.Chat;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._Persistence.Radio;

/// <summary>
/// Faction radio, ported from SS14-Persistence. Each faction picks which channels it has and which of its accesses
/// can use them (see the station modification console); headsets tuned to a faction talk over those channels.
/// Encryption keys keep working as normal for everything else.
/// </summary>
public sealed partial class FactionRadioSystem : EntitySystem
{
    [Dependency] private FactionSystem _faction = null!;
    [Dependency] private IdCardSystem _idCard = null!;
    [Dependency] private RadioSystem _radio = null!;
    [Dependency] private UserInterfaceSystem _ui = null!;

    /// <summary>
    /// The faction the message being sent right now is going out over, or 0 for a normal radio message.
    /// Radio messages are sent synchronously, so this is only ever set during <see cref="TrySendFactionMessage"/>.
    /// </summary>
    public int CurrentFaction { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HeadsetComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        Subs.BuiEvents<HeadsetComponent>(FactionHeadsetUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>((uid, _, args) => UpdateUserInterface(uid, args.Actor));
                subs.Event<FactionHeadsetTransmitSelect>(OnTransmitSelect);
                subs.Event<FactionHeadsetReceiveSelect>(OnReceiveSelect);
            });
    }

    #region Access

    /// <summary>
    /// Whether the user may use the channel on the faction's radio: it has to be enabled, and they have to own the
    /// faction or be on its records with an assignment granting one of the channel's accesses (any member, if the
    /// channel needs none).
    /// </summary>
    public bool HasChannelAccess(EntityUid user, EntityUid station, RadioChannelPrototype channel)
    {
        if (!TryComp<FactionDataComponent>(station, out var data)
            || !data.RadioData.TryGetValue(channel.ID, out var radio)
            || !radio.Enabled
            || !_idCard.TryFindIdCard(user, out var id)
            || id.Comp.FullName is not { } name)
        {
            return false;
        }

        if (data.IsOwner(name))
            return true;

        if (!_faction.HasRecord(name, station))
            return false;

        if (radio.Access.Count == 0)
            return true;

        var granted = _faction.GetGrantedAccesses(name, station);
        foreach (var access in radio.Access)
        {
            if (granted.Contains(access))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a radio should hear the message currently going out over the faction's radio.
    /// Only headsets do, if they're listening to that faction and their wearer has access to the channel.
    /// </summary>
    public bool CanReceive(EntityUid receiver, int factionId, RadioChannelPrototype channel)
    {
        if (!HasComp<HeadsetComponent>(receiver) || _faction.GetStationByID(factionId) is not { } station)
            return false;

        var receiveFrom = CompOrNull<FactionHeadsetComponent>(receiver)?.ReceiveFrom ?? 0;
        if (receiveFrom != 0 && receiveFrom != factionId)
            return false;

        return HasChannelAccess(Transform(receiver).ParentUid, station, channel);
    }

    /// <summary>
    /// Sends what the speaker said over their headset's faction radio, if it's tuned to a faction they can use the
    /// channel on. Returns false to let the encryption keys handle it otherwise.
    /// </summary>
    public bool TrySendFactionMessage(EntityUid speaker,
        EntityUid headset,
        SpeechMessage message,
        RadioChannelPrototype channel,
        LanguagePrototype? language,
        HeadsetLoudModeComponent? loudComp)
    {
        if (!TryComp<FactionHeadsetComponent>(headset, out var factionHeadset)
            || factionHeadset.TransmitTo == 0
            || _faction.GetStationByID(factionHeadset.TransmitTo) is not { } station
            || !HasChannelAccess(speaker, station, channel))
        {
            return false;
        }

        CurrentFaction = factionHeadset.TransmitTo;
        try
        {
            _radio.SendRadioMessage(speaker, message, channel, headset, language, loudComp: loudComp);
        }
        finally
        {
            CurrentFaction = 0;
        }

        return true;
    }

    #endregion

    #region UI

    private void OnGetVerbs(Entity<HeadsetComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("faction-headset-verb"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/settings.svg.192dpi.png")),
            Act = () => _ui.TryOpenUi(ent.Owner, FactionHeadsetUiKey.Key, user),
        });
    }

    private void UpdateUserInterface(EntityUid headset, EntityUid user)
    {
        var factions = new Dictionary<int, string>();
        if (_idCard.TryFindIdCard(user, out var id) && id.Comp.FullName is { } name)
        {
            foreach (var station in _faction.GetStationsAvailableTo(name))
            {
                factions[_faction.GetStationID(station)] = Name(station);
            }
        }

        var comp = CompOrNull<FactionHeadsetComponent>(headset);
        _ui.SetUiState(headset,
            FactionHeadsetUiKey.Key,
            new FactionHeadsetBoundUserInterfaceState(factions, comp?.TransmitTo ?? 0, comp?.ReceiveFrom ?? 0));
    }

    private void OnTransmitSelect(Entity<HeadsetComponent> ent, ref FactionHeadsetTransmitSelect args)
    {
        var comp = EnsureComp<FactionHeadsetComponent>(ent);
        comp.TransmitTo = args.Faction;
        Dirty(ent, comp);
        UpdateUserInterface(ent, args.Actor);
    }

    private void OnReceiveSelect(Entity<HeadsetComponent> ent, ref FactionHeadsetReceiveSelect args)
    {
        var comp = EnsureComp<FactionHeadsetComponent>(ent);
        comp.ReceiveFrom = args.Faction;
        Dirty(ent, comp);
        UpdateUserInterface(ent, args.Actor);
    }

    #endregion
}
