using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Popups;
using Content.Shared._Persistence.Factions.BUI;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Access;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Robust.Server.GameObjects;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// The station modification console, which a faction's owners use to run it. Ported from SS14-Persistence.
/// </summary>
public sealed partial class FactionSystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = null!;
    [Dependency] private FactionIdCardSystem _factionIdCard = null!;
    [Dependency] private PopupSystem _popup = null!;
    [Dependency] private UserInterfaceSystem _ui = null!;

    public const int MaxNameLength = 24;
    public const int MaxImportTax = 200;
    public const int MaxTax = 100;

    private void InitializeStationModification()
    {
        SubscribeLocalEvent<StationModificationConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);

        Subs.BuiEvents<StationModificationConsoleComponent>(StationModUiKey.StationMod,
            subs =>
            {
                subs.Event<StationModificationChangeName>(OnChangeName);
                subs.Event<StationModificationChangeFactionTag>(OnChangeFactionTag);
                subs.Event<StationModificationAddOwner>(OnAddOwner);
                subs.Event<StationModificationRemoveOwner>(OnRemoveOwner);
                subs.Event<StationModificationAddAccess>(OnAddAccess);
                subs.Event<StationModificationRemoveAccess>(OnRemoveAccess);
                subs.Event<StationModificationDefaultAccess>(OnDefaultAccess);
                subs.Event<StationModificationSetJobNet>(OnSetJobNet);
                subs.Event<StationModificationChangeTax>(OnChangeTax);
                subs.Event<StationModificationCreateAssignment>(OnCreateAssignment);
                subs.Event<StationModificationDeleteAssignment>(OnDeleteAssignment);
                subs.Event<StationModificationChangeAssignmentName>(OnChangeAssignmentName);
                subs.Event<StationModificationChangeAssignmentValue>(OnChangeAssignmentValue);
                subs.Event<StationModificationToggleAssignmentFlag>(OnToggleAssignmentFlag);
                subs.Event<StationModificationSetAssignmentAccess>(OnSetAssignmentAccess);
                subs.Event<StationModificationSetChannelEnabled>(OnSetChannelEnabled);
                subs.Event<StationModificationSetChannelAccess>(OnSetChannelAccess);
                subs.Event<StationModificationPurchaseUpgrade>(OnPurchaseUpgrade);
            });
    }

    #region Helpers

    /// <summary>
    /// Checks the console is on a faction and the user may run it: its owners can, as can anyone while the faction
    /// has no owners at all.
    /// </summary>
    private bool Validate(EntityUid console, EntityUid user, out Entity<FactionDataComponent> faction)
    {
        faction = default;
        if (GetOwningFaction(console) is not { } station || !TryComp<FactionDataComponent>(station, out var data))
        {
            ConsolePopup(user, "station-modification-no-faction");
            return false;
        }

        faction = (station, data);
        if (data.Owners.Count > 0 && !data.IsOwner(Name(user)))
        {
            ConsolePopup(user, "station-modification-access-denied");
            return false;
        }

        return true;
    }

    private bool TryGetAssignment(Entity<FactionDataComponent> faction,
        int id,
        EntityUid user,
        [NotNullWhen(true)] out CrewAssignmentsComponent? assignments,
        [NotNullWhen(true)] out CrewAssignment? assignment)
    {
        assignment = null;
        if (!TryComp(faction, out assignments) || !assignments.TryGetAssignment(id, out assignment))
        {
            ConsolePopup(user, "station-modification-invalid-assignment");
            return false;
        }

        return true;
    }

    private bool ValidateName(EntityUid user, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Length > MaxNameLength)
        {
            ConsolePopup(user, "station-modification-name-too-long", MaxNameLength);
            return false;
        }

        return true;
    }

    private bool AssignmentNameTaken(CrewAssignmentsComponent assignments, string name)
    {
        return assignments.CrewAssignments.Values.Any(a => a.Name == name);
    }

    private void ConsolePopup(EntityUid user, string message, int? max = null)
    {
        _popup.PopupCursor(max == null ? Loc.GetString(message) : Loc.GetString(message, ("max", max.Value)), user);
    }

    private void FactionChanged(EntityUid station, Component comp, EntityUid user, string change)
    {
        Dirty(station, comp);
        _adminLogger.Add(LogType.Action,
            LogImpact.Medium,
            $"{ToPrettyString(user):player} changed faction {ToPrettyString(station)}: {change}");
        UpdateConsoles(station);
    }

    #endregion

    #region General

    private void OnChangeName(Entity<StationModificationConsoleComponent> ent, ref StationModificationChangeName args)
    {
        var name = args.Name.Trim();
        if (!Validate(ent, args.Actor, out var faction) || !ValidateName(args.Actor, name))
            return;

        _station.RenameStation(faction, name, loud: false);
        FactionChanged(faction, faction.Comp, args.Actor, $"renamed to {name}");
        _factionIdCard.RefreshStationIds(faction.Comp.UID);
    }

    private void OnChangeFactionTag(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationChangeFactionTag args)
    {
        if (!Validate(ent, args.Actor, out var faction))
            return;

        // Normalizing keeps this consistent even if client-side filtering is bypassed.
        var normalized = FactionDataComponent.NormalizeFactionTag(args.Tag);

        // Clearing the custom tag falls back to the generated one, which mustn't collide either.
        var resolved = string.IsNullOrEmpty(normalized)
            ? FactionDataComponent.GenerateFactionTag(Name(faction))
            : normalized;

        if (FactionTagExistsOnAnotherStation(faction, resolved))
        {
            _popup.PopupCursor(Loc.GetString("station-modification-tag-taken", ("tag", resolved)), args.Actor);
            return;
        }

        faction.Comp.FactionTag = string.IsNullOrEmpty(normalized) ? null : normalized;
        FactionChanged(faction, faction.Comp, args.Actor, $"tag set to {resolved}");

        // Refresh issued IDs so the new tag shows up right away.
        _factionIdCard.RefreshStationIds(faction.Comp.UID);
    }

    public bool FactionTagExistsOnAnotherStation(EntityUid station, string candidateTag)
    {
        var normalizedCandidate = FactionDataComponent.NormalizeFactionTag(candidateTag);
        if (string.IsNullOrEmpty(normalizedCandidate))
            return false;

        var query = EntityQueryEnumerator<FactionDataComponent>();
        while (query.MoveNext(out var other, out var otherData))
        {
            if (other == station)
                continue;

            var otherResolved = otherData.GetResolvedFactionTag(Name(other));
            if (string.Equals(otherResolved, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void OnAddOwner(Entity<StationModificationConsoleComponent> ent, ref StationModificationAddOwner args)
    {
        var owner = args.Owner.Trim();
        if (!Validate(ent, args.Actor, out var faction) || string.IsNullOrEmpty(owner))
            return;

        if (faction.Comp.IsOwner(owner))
        {
            ConsolePopup(args.Actor, "station-modification-owner-exists");
            return;
        }

        faction.Comp.Owners.Add(owner);
        FactionChanged(faction, faction.Comp, args.Actor, $"added owner {owner}");
    }

    private void OnRemoveOwner(Entity<StationModificationConsoleComponent> ent, ref StationModificationRemoveOwner args)
    {
        if (!Validate(ent, args.Actor, out var faction))
            return;

        if (args.Owner == Name(args.Actor))
        {
            ConsolePopup(args.Actor, "station-modification-remove-self");
            return;
        }

        if (faction.Comp.Owners.Remove(args.Owner))
            FactionChanged(faction, faction.Comp, args.Actor, $"removed owner {args.Owner}");
    }

    private void OnAddAccess(Entity<StationModificationConsoleComponent> ent, ref StationModificationAddAccess args)
    {
        var access = args.Access.Trim();
        if (!Validate(ent, args.Actor, out var faction)
            || !ValidateName(args.Actor, access)
            || !TryComp<CrewAccessesComponent>(faction, out var accesses))
        {
            return;
        }

        if (!accesses.CrewAccesses.Add(access))
        {
            ConsolePopup(args.Actor, "station-modification-access-exists");
            return;
        }

        FactionChanged(faction, accesses, args.Actor, $"added access {access}");
    }

    private void OnRemoveAccess(Entity<StationModificationConsoleComponent> ent, ref StationModificationRemoveAccess args)
    {
        if (!Validate(ent, args.Actor, out var faction)
            || !TryComp<CrewAccessesComponent>(faction, out var accesses)
            || !accesses.CrewAccesses.Remove(args.Access))
        {
            return;
        }

        // Nothing should keep granting an access that no longer exists.
        if (TryComp<CrewAssignmentsComponent>(faction, out var assignments))
        {
            foreach (var assignment in assignments.CrewAssignments.Values)
            {
                assignment.AccessIDs.Remove(args.Access);
            }

            Dirty(faction, assignments);
        }

        foreach (var channel in faction.Comp.RadioData.Values)
        {
            channel.Access.Remove(args.Access);
        }

        Dirty(faction);
        FactionChanged(faction, accesses, args.Actor, $"removed access {args.Access}");
    }

    private void OnDefaultAccess(Entity<StationModificationConsoleComponent> ent, ref StationModificationDefaultAccess args)
    {
        if (!Validate(ent, args.Actor, out var faction) || !TryComp<CrewAccessesComponent>(faction, out var accesses))
            return;

        foreach (var level in _proto.EnumeratePrototypes<AccessLevelPrototype>())
        {
            if (level.CanAddToIdCard)
                accesses.CrewAccesses.Add(level.ID);
        }

        FactionChanged(faction, accesses, args.Actor, "added the default accesses");
    }

    private void OnSetJobNet(Entity<StationModificationConsoleComponent> ent, ref StationModificationSetJobNet args)
    {
        if (!Validate(ent, args.Actor, out var faction) || faction.Comp.JobNetEnabled == args.Enabled)
            return;

        faction.Comp.JobNetEnabled = args.Enabled;
        FactionChanged(faction, faction.Comp, args.Actor, args.Enabled ? "enabled JobNet" : "disabled JobNet");

        if (!args.Enabled)
        {
            var ev = new FactionJobNetDisabledEvent();
            RaiseLocalEvent(faction, ref ev);
        }
    }

    private void OnChangeTax(Entity<StationModificationConsoleComponent> ent, ref StationModificationChangeTax args)
    {
        var max = args.Type == FactionTaxType.Import ? MaxImportTax : MaxTax;
        if (args.Value < 0 || args.Value > max || !Validate(ent, args.Actor, out var faction))
            return;

        // Taxes only do anything at a trade station, see the cargo systems.
        if (GetStationTradeStation(faction) == null)
            return;

        switch (args.Type)
        {
            case FactionTaxType.Import:
                faction.Comp.ImportTax = args.Value;
                break;
            case FactionTaxType.Export:
                faction.Comp.ExportTax = args.Value;
                break;
            case FactionTaxType.Sales:
                faction.Comp.SalesTax = args.Value;
                break;
        }

        FactionChanged(faction, faction.Comp, args.Actor, $"{args.Type} tax set to {args.Value}%");
    }

    private void OnPurchaseUpgrade(Entity<StationModificationConsoleComponent> ent, ref StationModificationPurchaseUpgrade args)
    {
        if (!Validate(ent, args.Actor, out var faction)
            || !_proto.Resolve(faction.Comp.Level, out var current)
            || current.Next is not { } nextId
            || !_proto.Resolve(nextId, out var next))
        {
            return;
        }

        if (!TryAdjustFactionBalance(faction, -next.Cost))
        {
            ConsolePopup(args.Actor, "station-modification-insufficient-funds");
            return;
        }

        faction.Comp.Level = nextId;
        FactionChanged(faction, faction.Comp, args.Actor, $"bought level {nextId}");
    }

    #endregion

    #region Assignments

    private void OnCreateAssignment(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationCreateAssignment args)
    {
        var name = args.Name.Trim();
        if (!Validate(ent, args.Actor, out var faction)
            || !ValidateName(args.Actor, name)
            || !TryComp<CrewAssignmentsComponent>(faction, out var assignments))
        {
            return;
        }

        if (AssignmentNameTaken(assignments, name))
        {
            ConsolePopup(args.Actor, "station-modification-assignment-exists");
            return;
        }

        assignments.CreateAssignment(name);
        FactionChanged(faction, assignments, args.Actor, $"created assignment {name}");
    }

    private void OnDeleteAssignment(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationDeleteAssignment args)
    {
        if (!Validate(ent, args.Actor, out var faction)
            || !TryGetAssignment(faction, args.Assignment, args.Actor, out var assignments, out var assignment))
        {
            return;
        }

        assignments.CrewAssignments.Remove(args.Assignment);
        FactionChanged(faction, assignments, args.Actor, $"deleted assignment {assignment.Name}");
        _factionIdCard.RefreshStationIds(faction.Comp.UID);
    }

    private void OnChangeAssignmentName(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationChangeAssignmentName args)
    {
        var name = args.Name.Trim();
        if (!Validate(ent, args.Actor, out var faction)
            || !ValidateName(args.Actor, name)
            || !TryGetAssignment(faction, args.Assignment, args.Actor, out var assignments, out var assignment))
        {
            return;
        }

        if (AssignmentNameTaken(assignments, name))
        {
            ConsolePopup(args.Actor, "station-modification-assignment-exists");
            return;
        }

        var old = assignment.Name;
        assignment.Name = name;
        FactionChanged(faction, assignments, args.Actor, $"renamed assignment {old} to {name}");
        _factionIdCard.RefreshStationIds(faction.Comp.UID);
    }

    private void OnChangeAssignmentValue(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationChangeAssignmentValue args)
    {
        if (args.Value < 0
            || !Validate(ent, args.Actor, out var faction)
            || !TryGetAssignment(faction, args.Assignment, args.Actor, out var assignments, out var assignment))
        {
            return;
        }

        switch (args.Type)
        {
            case AssignmentValue.Wage:
                assignment.Wage = args.Value;
                break;
            case AssignmentValue.CommandLevel:
                assignment.Clevel = args.Value;
                break;
            case AssignmentValue.SpendingLimit:
                assignment.SpendingLimit = args.Value;
                break;
        }

        FactionChanged(faction, assignments, args.Actor, $"set {assignment.Name}'s {args.Type} to {args.Value}");
    }

    private void OnToggleAssignmentFlag(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationToggleAssignmentFlag args)
    {
        if (!Validate(ent, args.Actor, out var faction)
            || !TryGetAssignment(faction, args.Assignment, args.Actor, out var assignments, out var assignment))
        {
            return;
        }

        var value = args.Flag switch
        {
            AssignmentFlag.CanAssign => assignment.CanAssign = !assignment.CanAssign,
            AssignmentFlag.CanClaim => assignment.CanClaim = !assignment.CanClaim,
            AssignmentFlag.CanEditRecords => assignment.CanEditGeneralRecord = !assignment.CanEditGeneralRecord,
            _ => false,
        };

        FactionChanged(faction, assignments, args.Actor, $"set {assignment.Name}'s {args.Flag} to {value}");
    }

    private void OnSetAssignmentAccess(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationSetAssignmentAccess args)
    {
        if (!Validate(ent, args.Actor, out var faction)
            || !TryGetAssignment(faction, args.Assignment, args.Actor, out var assignments, out var assignment)
            || !TryComp<CrewAccessesComponent>(faction, out var accesses)
            || !accesses.CrewAccesses.Contains(args.Access))
        {
            return;
        }

        if (args.Enabled == assignment.AccessIDs.Contains(args.Access))
            return;

        if (args.Enabled)
            assignment.AccessIDs.Add(args.Access);
        else
            assignment.AccessIDs.Remove(args.Access);

        FactionChanged(faction,
            assignments,
            args.Actor,
            $"{(args.Enabled ? "gave" : "took")} {assignment.Name} access {args.Access}");
    }

    #endregion

    #region Radio

    private void OnSetChannelEnabled(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationSetChannelEnabled args)
    {
        if (!Validate(ent, args.Actor, out var faction)
            || !faction.Comp.RadioData.TryGetValue(args.Channel, out var channel)
            || channel.Enabled == args.Enabled)
        {
            return;
        }

        channel.Enabled = args.Enabled;
        FactionChanged(faction, faction.Comp, args.Actor, $"{(args.Enabled ? "enabled" : "disabled")} {args.Channel}");
    }

    private void OnSetChannelAccess(Entity<StationModificationConsoleComponent> ent,
        ref StationModificationSetChannelAccess args)
    {
        if (!Validate(ent, args.Actor, out var faction)
            || !faction.Comp.RadioData.TryGetValue(args.Channel, out var channel)
            || args.Enabled == channel.Access.Contains(args.Access))
        {
            return;
        }

        if (args.Enabled)
            channel.Access.Add(args.Access);
        else
            channel.Access.Remove(args.Access);

        FactionChanged(faction,
            faction.Comp,
            args.Actor,
            $"{(args.Enabled ? "added" : "removed")} {args.Access} on {args.Channel}");
    }

    #endregion

    #region UI

    private void OnConsoleOpened(Entity<StationModificationConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateConsole(ent);
    }

    /// <summary>
    /// Updates every station modification console on the faction's grids.
    /// </summary>
    public void UpdateConsoles(EntityUid station)
    {
        var query = EntityQueryEnumerator<StationModificationConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (GetOwningFaction(uid) == station)
                UpdateConsole(uid);
        }
    }

    private void UpdateConsole(EntityUid console)
    {
        if (!_ui.HasUi(console, StationModUiKey.StationMod))
            return;

        if (GetOwningFaction(console) is not { } station || !TryComp<FactionDataComponent>(station, out var data))
        {
            _ui.SetUiState(console, StationModUiKey.StationMod, new StationModificationInterfaceState { HasFaction = false });
            return;
        }

        var state = new StationModificationInterfaceState
        {
            Name = Name(station),
            FactionTag = data.GetResolvedFactionTag(Name(station)),
            Owners = data.Owners.ToList(),
            CrewAccesses = CompOrNull<CrewAccessesComponent>(station)?.CrewAccesses.OrderBy(a => a).ToList() ?? new(),
            CrewAssignments = CompOrNull<CrewAssignmentsComponent>(station)?.CrewAssignments ?? new(),
            ImportTax = data.ImportTax,
            ExportTax = data.ExportTax,
            SalesTax = data.SalesTax,
            Level = data.Level,
            AccountBalance = GetFactionBalance(station),
            RadioData = data.RadioData,
            JobNetEnabled = data.JobNetEnabled,
            TradeStationClaimed = GetStationTradeStation(station) != null,
        };

        _ui.SetUiState(console, StationModUiKey.StationMod, state);
    }

    #endregion
}

/// <summary>
/// Raised on a faction when its owners turn JobNet off, so everyone working for it can be clocked out.
/// </summary>
[ByRefEvent]
public record struct FactionJobNetDisabledEvent;
