using System.Linq;
using Content.Shared._Persistence.Factions.Components;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._Persistence.Factions;

/// <summary>
/// Lets faction membership open access readers, on top of the access levels on someone's ID.
/// On a faction's grids, a reader lets someone through if:
/// <list type="bullet">
/// <item>they own the faction,</item>
/// <item>their assignment grants access levels that satisfy the reader's normal access lists,</item>
/// <item>their assignment grants one of the faction accesses on the reader's <see cref="FactionAccessReaderComponent"/>,</item>
/// <item>or the reader is in personal mode and they're on its list.</item>
/// </list>
/// Who someone is comes from the name on their ID.
/// </summary>
public sealed partial class FactionAccessSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = null!;
    [Dependency] private SharedContainerSystem _container = null!;
    [Dependency] private AccessReaderSystem _accessReader = null!;
    [Dependency] private SharedFactionSystem _faction = null!;

    /// <summary>
    /// Whether the reader has faction requirements that someone has to meet, which stops a reader with no access
    /// lists from letting everyone through.
    /// </summary>
    public bool HasFactionRequirements(EntityUid reader)
    {
        return TryComp<FactionAccessReaderComponent>(reader, out var comp) && comp.HasRequirements;
    }

    /// <summary>
    /// Checks the faction side of an access reader, for when the user's own access levels weren't enough.
    /// </summary>
    /// <param name="items">The user's potential access items, see <see cref="AccessReaderSystem.FindPotentialAccessItems"/>.</param>
    /// <param name="tags">The access levels the user already has.</param>
    public bool IsAllowed(HashSet<EntityUid> items,
        ICollection<ProtoId<AccessLevelPrototype>> tags,
        EntityUid target,
        AccessReaderComponent reader)
    {
        if (_faction.GetIdName(items) is not { } name)
            return false;

        if (reader.ContainerAccessProvider == null)
            return IsAllowedReader(name, tags, (target, reader));

        if (!_container.TryGetContainer(target, reader.ContainerAccessProvider, out var container))
            return false;

        foreach (var contained in container.ContainedEntities)
        {
            if (TryComp<AccessReaderComponent>(contained, out var containedReader)
                && containedReader.Enabled
                && IsAllowedReader(name, tags, (contained, containedReader)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAllowedReader(string name,
        ICollection<ProtoId<AccessLevelPrototype>> tags,
        Entity<AccessReaderComponent> reader)
    {
        TryComp<FactionAccessReaderComponent>(reader, out var factionReader);

        if (factionReader is { PersonalAccessMode: true } && factionReader.PersonalAccessNames.Contains(name))
            return true;

        if (_faction.GetOwningFaction(reader) is not { } station)
            return false;

        if (_faction.IsOwner(name, station))
            return true;

        var granted = _faction.GetGrantedAccesses(name, station);
        if (granted.Count == 0)
            return false;

        if (factionReader != null && factionReader.AccessNames.Any(granted.Contains))
            return true;

        // Assignments can also grant normal access levels, which count for the reader's usual access lists.
        var combined = new HashSet<ProtoId<AccessLevelPrototype>>(tags);
        foreach (var access in granted)
        {
            if (_proto.HasIndex<AccessLevelPrototype>(access))
                combined.Add(access);
        }

        if (combined.Count == tags.Count)
            return false;

        return _accessReader.AreAccessTagsAllowed(combined, reader);
    }

    #region Configuration

    public void ToggleAccess(Entity<FactionAccessReaderComponent?> ent, string access)
    {
        ent.Comp ??= EnsureComp<FactionAccessReaderComponent>(ent);

        if (!ent.Comp.AccessNames.Remove(access))
            ent.Comp.AccessNames.Add(access);

        Dirty(ent);
    }

    public void SetPersonalMode(Entity<FactionAccessReaderComponent?> ent, bool enabled)
    {
        ent.Comp ??= EnsureComp<FactionAccessReaderComponent>(ent);
        ent.Comp.PersonalAccessMode = enabled;
        Dirty(ent);
    }

    public void AddPersonalAccess(Entity<FactionAccessReaderComponent?> ent, string name)
    {
        ent.Comp ??= EnsureComp<FactionAccessReaderComponent>(ent);
        if (string.IsNullOrWhiteSpace(name) || ent.Comp.PersonalAccessNames.Contains(name))
            return;

        ent.Comp.PersonalAccessNames.Add(name);
        Dirty(ent);
    }

    public void RemovePersonalAccess(Entity<FactionAccessReaderComponent?> ent, string name)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.PersonalAccessNames.Remove(name))
            return;

        Dirty(ent);
    }

    #endregion
}
