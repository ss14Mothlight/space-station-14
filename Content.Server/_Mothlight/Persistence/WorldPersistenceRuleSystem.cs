using System.IO;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Mothlight.Persistence;

/// <summary>
/// Loads the round's main map from a save instead of generating it, and saves it back at round end and on a timer.
/// Only does anything while <see cref="WorldPersistenceRuleComponent"/> is part of the current preset, so other game
/// modes and dev environments are unaffected.
/// </summary>
public sealed partial class WorldPersistenceRuleSystem : GameRuleSystem<WorldPersistenceRuleComponent>
{
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private CharacterPersistenceSystem _characters = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LoadingMapsEvent>(OnLoadingMaps);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnLoadingMaps(LoadingMapsEvent ev)
    {
        // Preset rules are only added (not started) when the maps get loaded.
        var query = QueryAllRules();
        while (query.MoveNext(out var uid, out var comp, out _))
        {
            if (!_res.UserData.Exists(comp.SavePath))
            {
                Log.Info($"No world save at {comp.SavePath}, generating a fresh map.");
                return;
            }

            // References to things that aren't part of the world (other maps, players' characters) are expected
            // to come back invalid, so don't log every one of them.
            var opts = DeserializationOptions.Default with { LogInvalidEntities = false };

            Entity<MapComponent>? map;
            try
            {
                if (!_loader.TryLoadMap(comp.SavePath, out map, out _, opts))
                    map = null;
            }
            catch (Exception e)
            {
                Log.Error($"Exception while loading the world save at {comp.SavePath}:\n{e}");
                map = null;
            }

            if (map == null)
            {
                // Don't let this round's fresh map overwrite the world that failed to load.
                comp.SavingDisabled = true;
                Log.Error($"Failed to load the world save at {comp.SavePath}. Generating a fresh map instead, " +
                          "and saving is disabled for this round so the existing save isn't overwritten.");
                Chat.SendAdminAnnouncement(Loc.GetString("world-persistence-load-failed"));
                return;
            }

            comp.LoadedFromSave = true;
            ev.LoadedDefaultMap = map.Value.Comp.MapId;
            Log.Info($"Loaded world save {comp.SavePath} as {ToPrettyString(map)}");
            return;
        }
    }

    protected override void Started(EntityUid uid,
        WorldPersistenceRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.AutosaveInterval is { } interval)
            component.NextAutosave = Timing.CurTime + interval;
    }

    protected override void ActiveTick(EntityUid uid,
        WorldPersistenceRuleComponent component,
        GameRuleComponent gameRule,
        float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.AutosaveInterval is not { } interval || Timing.CurTime < component.NextAutosave)
            return;

        component.NextAutosave = Timing.CurTime + interval;
        if (TrySaveWorld((uid, component)))
            Chat.DispatchServerAnnouncement(Loc.GetString("world-persistence-autosaved"));
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New != GameRunLevel.PostRound)
            return;

        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var comp, out _))
        {
            if (comp.SaveOnRoundEnd)
                TrySaveWorld((uid, comp));
        }
    }

    public bool HasActiveRule()
    {
        return QueryActiveRules().MoveNext(out _, out _, out _, out _);
    }

    /// <summary>
    /// Saves every active world persistence rule's world. Returns false if there is no active rule or a save failed.
    /// </summary>
    public bool TrySaveActiveWorlds()
    {
        var any = false;
        var ok = true;
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var comp, out _))
        {
            any = true;
            ok &= TrySaveWorld((uid, comp));
        }

        return any && ok;
    }

    /// <summary>
    /// Saves the round's main map, along with every player character currently in the round.
    /// </summary>
    public bool TrySaveWorld(Entity<WorldPersistenceRuleComponent> rule)
    {
        if (rule.Comp.SavingDisabled)
        {
            Log.Error($"Not saving the world to {rule.Comp.SavePath}, the existing save failed to load this round.");
            return false;
        }

        // Player-controlled mobs are kept out of map saves, so save them as characters instead.
        var characters = EntityQueryEnumerator<PersistentCharacterComponent, MindContainerComponent>();
        while (characters.MoveNext(out var uid, out var character, out var mind))
        {
            if (mind.HasMind)
                _characters.TrySaveCharacter((uid, character));
        }

        if (!_map.TryGetMap(GameTicker.DefaultMap, out var map))
        {
            Log.Error($"Can't save the world, the default map {GameTicker.DefaultMap} doesn't exist.");
            return false;
        }

        var writer = new StringWriter();
        if (!_loader.TrySaveMap(map.Value, writer))
        {
            Log.Error($"Failed to save the world {ToPrettyString(map)}, keeping the previous save.");
            return false;
        }

        // Keep the previous save around in case this one turns out to be broken.
        var path = rule.Comp.SavePath;
        var backup = path.WithName(path.Filename + ".bak");
        _res.UserData.CreateDir(path.Directory);
        if (_res.UserData.Exists(path))
        {
            if (_res.UserData.Exists(backup))
                _res.UserData.Delete(backup);
            _res.UserData.Rename(path, backup);
        }

        using (var file = _res.UserData.OpenWriteText(path))
        {
            file.Write(writer.ToString());
        }

        Log.Info($"Saved world {ToPrettyString(map)} to {path}");
        return true;
    }
}
