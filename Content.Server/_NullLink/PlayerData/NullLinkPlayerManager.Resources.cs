using System.Threading.Tasks;
using Content.Shared._NullLink;
using Robust.Shared.Player;
using Starlight.NullLink.Event;

namespace Content.Server._NullLink.PlayerData;

public sealed partial class NullLinkPlayerManager : INullLinkPlayerManager
{
    public ValueTask SyncResources(PlayerResourcesSyncEvent ev)
    {
        if (!_resourcesEnabled
            || !_playerById.TryGetValue(ev.Player, out var playerData))
            return ValueTask.CompletedTask;
        // Mothlight begin - credits belong to the character, not the account, so keep the character's
        playerData.Resources.TryGetValue("credits", out var credits);
        playerData.Resources.Clear();

        foreach (var resource in ev.Resources)
            playerData.Resources[resource.Key] = resource.Value;

        playerData.Resources["credits"] = credits;
        // Mothlight end

        SendPlayerResources(playerData.Session, playerData.Resources);
        _playerResourcesManager.TrySetResources(playerData.Session, playerData.Resources);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateResource(ResourceChangedEvent ev)
    {
        if (!_resourcesEnabled
            || !_playerById.TryGetValue(ev.Player, out var playerData)
            || ev.Resource == "credits") // Mothlight - credits belong to the character, not the account
            return ValueTask.CompletedTask;
        playerData.Resources[ev.Resource] = ev.NewAmount;

        SendPlayerResources(playerData.Session, playerData.Resources);
        _playerResourcesManager.TrySetResources(playerData.Session, playerData.Resources);
        return ValueTask.CompletedTask;
    }

    private void SendPlayerResources(ICommonSession session, Dictionary<string, double> resources)
        => _netMgr.ServerSendMessage(new MsgUpdatePlayerResources
        {
            Resources = resources
        }, session.Channel);
}
