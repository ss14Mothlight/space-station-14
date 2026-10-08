using System.Linq;
using Content.Server._Mothlight.Persistence;
using Content.Server.GameTicking.Events;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind.Components;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Roles;
using Content.Shared.Spawners.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

// ReSharper disable CheckNamespace
namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [Dependency] private CharacterPersistenceSystem _charPersistence = default!;
    [Dependency] private WorldPersistenceRuleSystem _worldPersistence = default!;
    [Dependency] private StationSystem _stationSystem = default!;

    private void SpawnPlayerPersistentLoad(ICommonSession player,
        HumanoidCharacterProfile character,
        EntityUid station,
        string? jobId = null,
        bool lateJoin = true,
        bool silent = false)
    {
        // Can't spawn players with a dummy ticker!
        if (DummyTicker)
            return;
        if (station == EntityUid.Invalid)
        {
            var stations = GetSpawnableStations();
            _robustRandom.Shuffle(stations);
            if (stations.Count == 0)
                station = EntityUid.Invalid;
            else
                station = stations[0];
        }

        if (lateJoin && DisallowLateJoin)
        {
            JoinAsObserver(player);
            return;
        }

        string speciesId;
        if (_randomizeCharacters)
        {
            var weightId = _cfg.GetCVar(CCVars.ICRandomSpeciesWeights);

            // If blank, choose a round start species.
            if (string.IsNullOrEmpty(weightId))
            {
                var speciesPrototypes = _prototypeManager.EnumeratePrototypes<SpeciesPrototype>();
                var roundStart = (from proto in speciesPrototypes where proto.RoundStart select proto.ID)
                    .Select(dummy => (ProtoId<SpeciesPrototype>)dummy).ToList();

                speciesId = roundStart.Count == 0
                    ? SharedHumanoidAppearanceSystem.DefaultSpecies
                    : _robustRandom.Pick(roundStart);
            }
            else
            {
                var weights = _prototypeManager.Index<WeightedRandomSpeciesPrototype>(weightId);
                speciesId = weights.Pick(_robustRandom);
            }

            character = HumanoidCharacterProfile.RandomWithSpecies(speciesId);
        }

        // We raise this event to allow other systems to handle spawning this player themselves. (e.g. late-join wizard, etc)
        var bev = new PlayerBeforeSpawnEvent(player, character, jobId, lateJoin, station);
        RaiseLocalEvent(bev);

        // Do nothing, something else has handled spawning this player for us!
        if (bev.Handled)
        {
            PlayerJoinGame(player, silent);
            return;
        }

        // Pick where they're going first, so a missing spawn point can't leave them stuck in nullspace.
        var possiblePositions = new List<EntityCoordinates>();
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (points.MoveNext(out _, out var spawnPoint, out var xform))
        {
            if (spawnPoint.SpawnType != SpawnPointType.LateJoin)
                continue;

            if (station.IsValid() && xform.GridUid is { } grid && _stationSystem.GetOwningStation(grid) != station)
                continue;

            possiblePositions.Add(xform.Coordinates);
        }

        if (possiblePositions.Count == 0)
        {
            _sawmill.Error($"No late-join spawn points to load {player.Name}'s character {character.Name} at.");
            return;
        }

        var spawnLoc = _robustRandom.Pick(possiblePositions);

        // On a persistent world, put them back where they left off.
        var preferSavedPosition = _worldPersistence.HasActiveRule();
        if (!_charPersistence.TryLoadCharacter(player.UserId, character, spawnLoc, station, out var loaded, preferSavedPosition))
        {
            // Nothing saved (or the save is broken), start them off fresh and save that instead.
            _chatManager.DispatchServerMessage(player, "No saved character found, spawning a new one.");
            SpawnPlayer(player, character, station, jobId, lateJoin, silent, save: true);
            return;
        }

        var mob = loaded.Value;

        PlayerJoinGame(player, silent);
        _playTimeTrackings.PlayerRolesChanged(player);

        var newMind = _mind.CreateMind(player.UserId, character.Name);
        _mind.SetUserId(newMind, player.UserId);
        newMind.Comp.Voice = character.Voice;
        newMind.Comp.SiliconVoice = character.SiliconVoice;
        _mind.TransferTo(newMind, mob);

        if (jobId != null)
        {
            _roles.MindAddJobRole(newMind, silent: silent, jobPrototype: jobId);
            if (station.IsValid())
                _stationJobs.TryAssignJob(station, jobId, player.UserId);
        }

        _admin.UpdatePlayerList(player);
        _adminLogger.Add(LogType.LateJoin,
            LogImpact.Medium,
            $"Player {player.Name} late joined as {character.Name:characterName} with {ToPrettyString(mob):entity}. Loaded saved character.");

        if (!silent && TryComp(station, out MetaDataComponent? metaData))
        {
            _chatManager.DispatchServerMessage(player,
                Loc.GetString("job-greet-station-name", ("stationName", metaData.EntityName)));
        }

        // We raise this event directed to the mob, but also broadcast it so game rules can do something now.
        PlayersJoinedRoundNormally++;
        var aev = new PlayerSpawnCompleteEvent(mob,
            player,
            jobId,
            lateJoin,
            silent,
            PlayersJoinedRoundNormally,
            station,
            character);
        RaiseLocalEvent(mob, aev, true);
    }

    /// <summary>
    /// Makes a player join into the game and spawn on a station.
    /// </summary>
    /// <param name="player">The player joining</param>
    /// <param name="station">The station they're spawning on</param>
    /// <param name="jobId">An optional job for them to spawn as</param>
    /// <param name="silent">Whether or not the player should be greeted upon joining</param>
    public void MakeJoinGamePersistent(ICommonSession player, EntityUid station, string? jobId = null, bool silent = false)
    {
        if (!_playerGameStatuses.ContainsKey(player.UserId))
            return;

        if (!_userDb.IsLoadComplete(player))
            return;

        SpawnPlayer(player, station, jobId, silent: silent, save: true);
    }

    /// <summary>
    /// Makes a player join into the game and spawn on a station
    /// </summary>
    /// <remarks>
    /// This is currently used by:
    /// RespawnRuleSystem (for like deathmatch I think?)
    /// Join Game command (late join)
    /// </remarks>
    /// <param name="player">The player joining</param>
    /// <param name="profile">The humanoid profile they're spawning with</param>
    /// <param name="station">The station they're spawning on</param>
    /// <param name="jobId">An optional job for them to spawn as</param>
    /// <param name="silent">Whether or not the player should be greeted upon joining</param>
    public void MakeJoinGamePersistent(ICommonSession player, HumanoidCharacterProfile profile, EntityUid station, string? jobId = null, bool silent = false)
    {
        if (!_playerGameStatuses.ContainsKey(player.UserId))
            return;

        if (!_userDb.IsLoadComplete(player))
            return;

        SpawnPlayer(player, profile, station, jobId, silent: silent, save: true);
    }

    public void MakeJoinGamePersistentLoad(ICommonSession player, HumanoidCharacterProfile profile, EntityUid station,
        string? jobId = null, bool silent = false)
    {
        if (!_playerGameStatuses.ContainsKey(player.UserId))
            return;

        if (!_userDb.IsLoadComplete(player))
            return;

        SpawnPlayerPersistentLoad(player, profile, station, jobId, silent: silent);
    }
}
