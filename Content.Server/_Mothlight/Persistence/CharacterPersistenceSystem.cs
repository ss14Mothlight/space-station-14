using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.Preferences;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Mothlight.Persistence;

/// <summary>
/// Saves and loads player characters (their body, inventory and components) to and from user data,
/// so they persist across rounds and server restarts.
/// </summary>
/// <remarks>
/// Appearance is never taken from the save: the current character profile is always re-applied on load,
/// so players can keep editing their character in the lobby. If the species changed, a fresh body is
/// spawned from the profile and the saved belongings + <see cref="CarriedOverComponents"/> are moved onto it.
/// </remarks>
public sealed partial class CharacterPersistenceSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = null!;
    [Dependency] private IPrototypeManager _proto = null!;
    [Dependency] private IResourceManager _res = null!;
    [Dependency] private MapLoaderSystem _loader = null!;
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = null!;
    [Dependency] private InventorySystem _inventory = null!;
    [Dependency] private MetaDataSystem _metaData = null!;
    [Dependency] private SharedHandsSystem _hands = null!;
    [Dependency] private SharedTransformSystem _transform = null!;
    [Dependency] private StationSpawningSystem _spawning = null!;
    [Dependency] private GameTicker _ticker = null!;
    [Dependency] private SharedMapSystem _map = null!;

    private static readonly ResPath SaveRoot = new("/Mothlight/Characters");

    /// <summary>
    /// Components copied from the old body onto the new one when a character's species has changed and the
    /// body has to be rebuilt. When the species is unchanged, the saved body is reused, so every component persists.
    /// Only add components here that don't reference other entities, or that are safe to copy across species.
    /// </summary>
    public static readonly string[] CarriedOverComponents =
    [
        "Damageable",
        "Hunger",
        "Thirst",
        "CharacterCredits",
    ];

    /// <summary>
    /// The mob currently being saved as a character, if any. Used to let it (and only it) past the
    /// "don't save player-controlled mobs" filter.
    /// </summary>
    private EntityUid? _savingCharacter;

    public override void Initialize()
    {
        base.Initialize();

        _loader.OnIsSerializable += OnIsSerializable;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _loader.OnIsSerializable -= OnIsSerializable;
    }

    #region Savability

    /// <summary>
    /// Upstream marks every species mob as <c>save: false</c>, which makes the map loader refuse to serialize them
    /// at all. Persistence saves (characters, the world) need them, so this makes humanoids savable until the
    /// returned scope is disposed. Everything else, like the map editor, keeps Starlight's behaviour.
    /// Player-controlled mobs still get excluded from map saves by <see cref="OnIsSerializable"/>.
    /// </summary>
    public HumanoidSavingScope AllowHumanoidSaving()
    {
        var humanoidName = _factory.GetComponentName<HumanoidAppearanceComponent>();
        var flipped = new List<EntityPrototype>();
        foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.MapSavable || !proto.Components.ContainsKey(humanoidName))
                continue;

            proto.MapSavable = true;
            flipped.Add(proto);
        }

        return new HumanoidSavingScope(flipped);
    }

    public readonly struct HumanoidSavingScope(List<EntityPrototype> flipped) : IDisposable
    {
        public void Dispose()
        {
            foreach (var proto in flipped)
            {
                proto.MapSavable = false;
            }
        }
    }

    private void OnIsSerializable(Entity<MetaDataComponent> ent, ref bool serializable)
    {
        if (!serializable || ent.Owner == _savingCharacter)
            return;

        // Player characters are saved separately, keep them out of map saves (and out of each other's saves,
        // e.g. a player-controlled mouse in someone's pocket).
        if (TryComp<MindContainerComponent>(ent, out var mind) && Exists(mind.Mind))
            serializable = false;
    }

    #endregion

    #region Paths

    public ResPath GetSavePath(NetUserId user, string profileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(profileName.Select(c => invalid.Contains(c) || c == '/' ? '_' : c).ToArray());
        return SaveRoot / user.ToString() / $"{safeName}.yml";
    }

    public bool HasSave(NetUserId user, string profileName)
    {
        return _res.UserData.Exists(GetSavePath(user, profileName));
    }

    #endregion

    #region Saving

    /// <summary>
    /// Tags a freshly spawned mob as the persistent character for this user + profile.
    /// </summary>
    public void MarkPersistent(EntityUid mob, NetUserId user, string profileName)
    {
        var comp = EnsureComp<PersistentCharacterComponent>(mob);
        comp.UserId = user;
        comp.ProfileName = profileName;
    }

    /// <summary>
    /// Saves a character marked with <see cref="PersistentCharacterComponent"/> to its save file.
    /// </summary>
    public bool TrySaveCharacter(Entity<PersistentCharacterComponent?> mob)
    {
        if (!Resolve(mob, ref mob.Comp, false))
        {
            Log.Error($"Tried to save {ToPrettyString(mob)} as a character, but it isn't a persistent character.");
            return false;
        }

        return TrySaveCharacter(mob, mob.Comp.UserId, mob.Comp.ProfileName);
    }

    public bool TrySaveCharacter(EntityUid mob, NetUserId user, string profileName)
    {
        MarkPersistent(mob, user, profileName);

        // Remember where they were, in case the world they were in persists too.
        var mapCoords = _transform.GetMapCoordinates(mob);
        Comp<PersistentCharacterComponent>(mob).LastWorldPosition =
            mapCoords.MapId == _ticker.DefaultMap ? mapCoords.Position : null;

        var opts = SerializationOptions.Default with
        {
            // The mob is (almost) always parented to a grid or container that we deliberately don't save.
            ErrorOnOrphan = false,
            // References to things outside the character (their Mind, the station, etc) are rebuilt on load, so
            // drop them instead of pulling random nullspace entities into the file.
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            LogAutoInclude = null,
        };

        var writer = new StringWriter();
        bool ok;
        FileCategory category;
        _savingCharacter = mob;
        using var humanoids = AllowHumanoidSaving();
        try
        {
            ok = _loader.TrySaveGeneric(mob, writer, out category, opts);
        }
        finally
        {
            _savingCharacter = null;
        }

        if (!ok || category != FileCategory.Entity)
        {
            Log.Error($"Failed to save character {ToPrettyString(mob)} for {user} (category: {category}).");
            return false;
        }

        // Only touch the file once serialization succeeded, so a failed save never clobbers a good one.
        var path = GetSavePath(user, profileName);
        _res.UserData.CreateDir(path.Directory);
        using (var file = _res.UserData.OpenWriteText(path))
        {
            file.Write(writer.ToString());
        }

        Log.Info($"Saved character {ToPrettyString(mob)} to {path}");
        return true;
    }

    #endregion

    #region Loading

    /// <summary>
    /// Loads a saved character and places it at <paramref name="coords"/>, applying the current
    /// profile's appearance and name.
    /// </summary>
    /// <param name="preferSavedPosition">
    /// Put the character back where it was saved instead, if that spot is still somewhere to stand.
    /// Only makes sense when the map itself persisted.
    /// </param>
    /// <returns>False if there is no save or it could not be loaded.</returns>
    public bool TryLoadCharacter(
        NetUserId user,
        HumanoidCharacterProfile profile,
        EntityCoordinates coords,
        EntityUid? station,
        [NotNullWhen(true)] out EntityUid? mob,
        bool preferSavedPosition = false)
    {
        mob = null;
        var path = GetSavePath(user, profile.Name);
        if (!_res.UserData.Exists(path))
            return false;

        // The references we dropped while saving (Mind, station, etc) are expected to be invalid.
        var opts = DeserializationOptions.Default with { LogInvalidEntities = false };
        if (!_loader.TryLoadEntity(path, out var loaded, opts))
        {
            Log.Error($"Failed to load character save {path}");
            return false;
        }

        var saved = loaded.Value.Owner;
        if (preferSavedPosition && TryGetSavedPosition(saved, out var savedCoords))
            coords = savedCoords;
        _transform.SetCoordinates(saved, coords);

        // The old mind reference was dropped while saving. The stale MindContainer data is harmless, it gets
        // overwritten as soon as the caller transfers a new mind in.

        if (MetaData(saved).EntityPrototype?.ID == GetBodyPrototype(profile))
        {
            // Same body type, reuse the saved body as-is and just put the profile's look back on it.
            _humanoid.LoadProfile(saved, profile);
            _metaData.SetEntityName(saved, profile.Name);
            mob = saved;
        }
        else
        {
            // Species changed, so the old body can't be reused. Build a new one and move everything over.
            var fresh = _spawning.SpawnPlayerMob(coords, null, profile, station);
            TransferBelongings(saved, fresh);
            CopyCarriedOverComponents(saved, fresh);
            Del(saved);
            mob = fresh;
        }

        MarkPersistent(mob.Value, user, profile.Name);
        return true;
    }

    /// <summary>
    /// Gets the spot on the main map the character was saved at, if there's still a floor there.
    /// </summary>
    private bool TryGetSavedPosition(EntityUid saved, out EntityCoordinates coords)
    {
        coords = default;
        if (CompOrNull<PersistentCharacterComponent>(saved)?.LastWorldPosition is not { } position)
            return false;

        var mapCoords = new MapCoordinates(position, _ticker.DefaultMap);
        if (!_map.TryFindGridAt(mapCoords, out var gridUid, out var grid)
            || !_map.TryGetTileRef(gridUid, grid, mapCoords.Position, out var tile)
            || tile.Tile.IsEmpty)
        {
            return false;
        }

        coords = _transform.ToCoordinates(gridUid, mapCoords);
        return true;
    }

    private string GetBodyPrototype(HumanoidCharacterProfile profile)
    {
        if (!string.IsNullOrEmpty(profile.ForcedPrototype))
            return profile.ForcedPrototype;

        return _proto.Index<SpeciesPrototype>(profile.Species).Prototype;
    }

    private void TransferBelongings(EntityUid from, EntityUid to)
    {
        // Unequip dependent slots (pockets, ID, belt...) before what they depend on, otherwise removing the
        // jumpsuit drops them. Then equip in the opposite order.
        var items = new List<(SlotDefinition Slot, EntityUid Item)>();
        if (_inventory.TryGetSlots(from, out var slots))
        {
            foreach (var slot in slots.OrderBy(s => s.DependsOn == null))
            {
                if (_inventory.TryUnequip(from, slot.Name, out var item, silent: true, force: true, reparent: false))
                    items.Add((slot, item.Value));
            }
        }

        var leftovers = new List<EntityUid>();
        items.Reverse();
        foreach (var (slot, item) in items)
        {
            // The fresh body may have spawned with species gear in this slot, the saved item wins.
            if (_inventory.TryUnequip(to, slot.Name, out var existing, silent: true, force: true))
                Del(existing.Value);

            if (!_inventory.TryEquip(to, item, slot.Name, silent: true, force: true))
                leftovers.Add(item);
        }

        foreach (var held in _hands.EnumerateHeld(from).ToList())
        {
            leftovers.Add(held);
        }

        // Anything that doesn't fit the new body goes in its hands, or failing that, on the floor.
        foreach (var item in leftovers)
        {
            if (!_hands.TryPickupAnyHand(to, item, checkActionBlocker: false, animate: false))
                _transform.DropNextTo(item, to);
        }
    }

    private void CopyCarriedOverComponents(EntityUid from, EntityUid to)
    {
        foreach (var name in CarriedOverComponents)
        {
            if (!_factory.TryGetRegistration(name, out var reg))
                continue;

            if (TryComp(from, reg.Type, out var comp))
                CopyComp(from, to, comp);
        }
    }

    #endregion
}
