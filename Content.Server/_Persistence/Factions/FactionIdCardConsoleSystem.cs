using System.Linq;
using Content.Server.Access.Systems;
using Content.Shared._Persistence.Factions.BUI;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Access.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Paper;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// The faction tabs of the ID card console, ported from SS14-Persistence. Lets a faction's owners and anyone allowed
/// to assign hand out assignments, reset spending, and keep general, criminal and medical records on the faction's
/// crew.
/// The ID config tab (name, job title, icon and accesses written to the card) is the normal ID card console.
/// </summary>
public sealed partial class FactionIdCardConsoleSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = null!;
    [Dependency] private FactionIdCardSystem _factionIdCard = null!;
    [Dependency] private FactionSystem _faction = null!;
    [Dependency] private IdCardConsoleSystem _console = null!;
    [Dependency] private MetaDataSystem _metaData = null!;
    [Dependency] private PaperSystem _paper = null!;

    private static readonly int MaxRecordLength = 8000;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IdCardConsoleComponent, FactionIdConsoleSelectMessage>(OnSelect);
        SubscribeLocalEvent<IdCardConsoleComponent, FactionIdConsoleAssignMessage>(OnAssign);
        SubscribeLocalEvent<IdCardConsoleComponent, FactionIdConsoleResetSpendingMessage>(OnResetSpending);
        SubscribeLocalEvent<IdCardConsoleComponent, FactionIdConsoleSaveRecordMessage>(OnSaveRecord);
        SubscribeLocalEvent<IdCardConsoleComponent, FactionIdConsolePrintRecordMessage>(OnPrintRecord);
    }

    #region State

    /// <summary>
    /// Builds the faction tabs' state for the console.
    /// </summary>
    public FactionIdCardConsoleState BuildState(Entity<IdCardConsoleComponent> console)
    {
        var state = new FactionIdCardConsoleState();
        var selection = EnsureComp<FactionIdCardConsoleComponent>(console);

        // A newly inserted target ID selects whoever it belongs to.
        var targetName = GetTargetName(console);
        if (targetName != selection.LastTargetName)
        {
            selection.LastTargetName = targetName;
            if (targetName != null)
                selection.SelectedName = targetName;
        }

        if (_faction.GetOwningFaction(console) is not { } station)
            return state;

        state.FactionName = Name(station);
        if (TryComp<CrewAssignmentsComponent>(station, out var assignments))
            state.Assignments = assignments.CrewAssignments;

        if (GetPrivilegedName(console) is not { } privileged || !_faction.HasRecord(privileged, station))
            return state;

        state.IsMember = true;
        state.IsOwner = _faction.IsOwner(privileged, station);
        if (_faction.TryGetAssignment(privileged, station, out var privilegedAssignment))
            state.PrivilegedAssignment = privilegedAssignment;

        if (TryComp<CrewRecordsComponent>(station, out var records))
        {
            state.Crew = records.CrewRecords.Values
                .OrderByDescending(r => r.LastPaid)
                .Select(r => new FactionCrewListEntry(r.Name,
                    state.Assignments.GetValueOrDefault(r.AssignmentID)?.Name,
                    r.LastPaid))
                .ToList();
        }

        state.CanEditRecords = _faction.CanEditGeneralRecord(privileged, station);

        if (selection.SelectedName is not { } selected)
            return state;

        state.SelectedName = selected;
        foreach (var id in state.Assignments.Keys.Append(0))
        {
            if (CanAssign(privileged, selected, station, id))
                state.AssignableIds.Add(id);
        }

        if (!_faction.TryGetRecord(selected, station, out var record))
            return state;

        state.SelectedHasRecord = true;
        state.SelectedAssignmentId = record.AssignmentID;
        state.SelectedSpent = record.Spent;
        state.CanResetSpending = record.Spent > 0 && _faction.CanSpend(privileged, station, record.Spent);
        state.GeneralRecord = record.GeneralRecord;
        state.CriminalRecord = record.CriminalRecord;
        state.MedicalRecord = record.MedicalRecord;

        return state;
    }

    private string? GetPrivilegedName(Entity<IdCardConsoleComponent> console)
    {
        return console.Comp.PrivilegedIdSlot.Item is { } id ? _faction.GetIdName(id) : null;
    }

    private string? GetTargetName(Entity<IdCardConsoleComponent> console)
    {
        return console.Comp.TargetIdSlot.Item is { } id ? _faction.GetIdName(id) : null;
    }

    /// <summary>
    /// Owners can hand out any assignment. Anyone else needs an assignment that can assign, and can only move people
    /// between assignments with a lower command level than their own.
    /// </summary>
    public bool CanAssign(string privileged, string target, EntityUid station, int newAssignment)
    {
        if (_faction.IsOwner(privileged, station))
            return true;

        if (privileged == target
            || !_faction.TryGetAssignment(privileged, station, out var own)
            || !own.CanAssign
            || !TryComp<CrewAssignmentsComponent>(station, out var assignments))
        {
            return false;
        }

        if (_faction.TryGetAssignment(target, station, out var current) && current.Clevel >= own.Clevel)
            return false;

        if (newAssignment == 0)
            return true;

        return assignments.TryGetAssignment(newAssignment, out var next) && next.Clevel < own.Clevel;
    }

    #endregion

    #region Messages

    /// <summary>
    /// Gets the faction, privileged name and selected person for a faction action, if the console has all three.
    /// </summary>
    private bool TryGetContext(Entity<IdCardConsoleComponent> console,
        out EntityUid station,
        out string privileged,
        out string selected)
    {
        station = default;
        privileged = string.Empty;
        selected = string.Empty;

        if (_faction.GetOwningFaction(console) is not { } faction
            || GetPrivilegedName(console) is not { } privilegedName
            || !_faction.HasRecord(privilegedName, faction)
            || CompOrNull<FactionIdCardConsoleComponent>(console)?.SelectedName is not { } selectedName)
        {
            return false;
        }

        station = faction;
        privileged = privilegedName;
        selected = selectedName;
        return true;
    }

    private void OnSelect(Entity<IdCardConsoleComponent> ent, ref FactionIdConsoleSelectMessage args)
    {
        var name = args.Name.Trim();
        EnsureComp<FactionIdCardConsoleComponent>(ent).SelectedName = name.Length == 0 ? null : name;
        _console.RefreshUserInterface(ent);
    }

    private void OnAssign(Entity<IdCardConsoleComponent> ent, ref FactionIdConsoleAssignMessage args)
    {
        if (!TryGetContext(ent, out var station, out var privileged, out var selected)
            || !CanAssign(privileged, selected, station, args.Assignment)
            || _faction.EnsureRecord(station, selected) is not { } record)
        {
            return;
        }

        _faction.SetAssignment(station, record, args.Assignment);
        _factionIdCard.RefreshIds(selected, _faction.GetStationID(station));

        var assignmentName = _faction.TryGetAssignment(selected, station, out var assignment)
            ? assignment.Name
            : "none";
        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} ({privileged}) set {selected}'s assignment in {ToPrettyString(station)} to {assignmentName}");

        _console.RefreshUserInterface(ent);
    }

    private void OnResetSpending(Entity<IdCardConsoleComponent> ent, ref FactionIdConsoleResetSpendingMessage args)
    {
        if (!TryGetContext(ent, out var station, out var privileged, out var selected)
            || !_faction.TryGetRecord(selected, station, out var record)
            || !_faction.CanSpend(privileged, station, record.Spent))
        {
            return;
        }

        // Resetting someone else's spending counts against your own limit.
        _faction.TrackSpending(privileged, station, record.Spent);
        _faction.ResetSpending(selected, station);
        _console.RefreshUserInterface(ent);
    }

    private void OnSaveRecord(Entity<IdCardConsoleComponent> ent, ref FactionIdConsoleSaveRecordMessage args)
    {
        if (!TryGetContext(ent, out var station, out var privileged, out var selected)
            || !_faction.CanEditGeneralRecord(privileged, station)
            || !_faction.TryGetRecord(selected, station, out var record))
        {
            return;
        }

        var content = args.Content.Length > MaxRecordLength ? args.Content[..MaxRecordLength] : args.Content;
        switch (args.Type)
        {
            case FactionRecordType.General:
                record.GeneralRecord = content;
                break;
            case FactionRecordType.Criminal:
                record.CriminalRecord = content;
                break;
            case FactionRecordType.Medical:
                record.MedicalRecord = content;
                break;
        }

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} ({privileged}) edited {selected}'s {args.Type} record in {ToPrettyString(station)}");
        _console.RefreshUserInterface(ent);
    }

    private void OnPrintRecord(Entity<IdCardConsoleComponent> ent, ref FactionIdConsolePrintRecordMessage args)
    {
        if (!TryGetContext(ent, out var station, out _, out var selected)
            || !_faction.TryGetRecord(selected, station, out var record))
        {
            return;
        }

        var (content, title) = args.Type switch
        {
            FactionRecordType.Criminal => (record.CriminalRecord, "faction-id-console-criminal-record-paper"),
            FactionRecordType.Medical => (record.MedicalRecord, "faction-id-console-medical-record-paper"),
            _ => (record.GeneralRecord, "faction-id-console-general-record-paper"),
        };

        var paper = Spawn("Paper", Transform(ent).Coordinates);
        if (TryComp<PaperComponent>(paper, out var paperComp))
        {
            _paper.SetContent((paper, paperComp), content);
            paperComp.EditingDisabled = true;
        }

        _metaData.SetEntityName(paper, Loc.GetString(title, ("name", record.Name)));
    }

    #endregion
}
