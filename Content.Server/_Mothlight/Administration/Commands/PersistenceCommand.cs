using Content.Server._Mothlight.Persistence;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Toolshed;
using Robust.Shared.Utility;

namespace Content.Server._Mothlight.Administration.Commands;

/// <summary>
/// Admin tools for persistent worlds and characters.
/// </summary>
[ToolshedCommand]
[AdminCommand(AdminFlags.Server)]
public sealed class PersistenceCommand : ToolshedCommand
{
    private WorldPersistenceRuleSystem? _world;
    private CharacterPersistenceSystem? _characters;
    private MapLoaderSystem? _loader;
    private SharedTransformSystem? _transform;

    /// <summary>
    /// Saves the persistent world, and everyone currently playing in it.
    /// </summary>
    [CommandImplementation("saveworld")]
    public bool SaveWorld(IInvocationContext ctx)
    {
        _world ??= GetSys<WorldPersistenceRuleSystem>();

        if (!_world.HasActiveRule())
        {
            ctx.WriteLine("There's no active world persistence rule. Is the round running the Persistence preset?");
            return false;
        }

        var ok = _world.TrySaveActiveWorlds();
        ctx.WriteLine(ok ? "World saved." : "Failed to save the world, check the server log.");
        return ok;
    }

    /// <summary>
    /// Saves the piped persistent characters to their save files.
    /// </summary>
    [CommandImplementation("savechar")]
    public IEnumerable<EntityUid> SaveCharacter(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> input)
    {
        _characters ??= GetSys<CharacterPersistenceSystem>();

        foreach (var uid in input)
        {
            if (!TryComp<PersistentCharacterComponent>(uid, out var character))
            {
                ctx.WriteLine($"{EntityManager.ToPrettyString(uid)} isn't a persistent character.");
                continue;
            }

            if (_characters.TrySaveCharacter((uid, character)))
                yield return uid;
            else
                ctx.WriteLine($"Failed to save {EntityManager.ToPrettyString(uid)}, check the server log.");
        }
    }

    /// <summary>
    /// Saves the piped entity, and everything it contains, to an arbitrary file in user data.
    /// </summary>
    [CommandImplementation("saveentity")]
    public EntityUid? SaveEntity(IInvocationContext ctx, [PipedArgument] EntityUid input, string path)
    {
        _loader ??= GetSys<MapLoaderSystem>();

        if (_loader.TrySaveGeneric(input, new ResPath(path), out var category))
        {
            ctx.WriteLine($"Saved {EntityManager.ToPrettyString(input)} to {path} as a {category}.");
            return input;
        }

        ctx.WriteLine($"Failed to save {EntityManager.ToPrettyString(input)}, check the server log.");
        return null;
    }

    /// <summary>
    /// Loads a single saved entity from user data, and places it where the caller is.
    /// </summary>
    [CommandImplementation("loadentity")]
    public EntityUid? LoadEntity(IInvocationContext ctx, string path)
    {
        _loader ??= GetSys<MapLoaderSystem>();
        _transform ??= GetSys<SharedTransformSystem>();

        if (!_loader.TryLoadEntity(new ResPath(path), out var entity))
        {
            ctx.WriteLine($"Failed to load an entity from {path}, check the server log.");
            return null;
        }

        if (ctx.Session?.AttachedEntity is { } caller)
            _transform.SetCoordinates(entity.Value, Transform(caller).Coordinates);
        else
            ctx.WriteLine("You aren't attached to an entity, so it was left in nullspace.");

        return entity.Value.Owner;
    }

    /// <summary>
    /// Loads a saved map from user data as a new map.
    /// </summary>
    [CommandImplementation("loadmap")]
    public EntityUid? LoadMap(IInvocationContext ctx, string path)
    {
        _loader ??= GetSys<MapLoaderSystem>();

        if (!_loader.TryLoadMap(new ResPath(path), out var map, out _))
        {
            ctx.WriteLine($"Failed to load a map from {path}, check the server log.");
            return null;
        }

        ctx.WriteLine($"Loaded {EntityManager.ToPrettyString(map)} as map {map.Value.Comp.MapId}.");
        return map.Value.Owner;
    }
}
