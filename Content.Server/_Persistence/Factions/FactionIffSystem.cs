using Content.Shared._Persistence.Factions.Components;
using Content.Server.Shuttles.Components;

namespace Content.Server._Persistence.Factions;

/// <summary>
/// Lets IFF consoles hide or show their grid's faction tag on radar. Ported from SS14-Persistence.
/// </summary>
public sealed partial class FactionIffSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IFFConsoleComponent, IFFShowFactionTagMessage>(OnShowFactionTag);
    }

    private void OnShowFactionTag(Entity<IFFConsoleComponent> ent, ref IFFShowFactionTagMessage args)
    {
        if (Transform(ent).GridUid is not { } grid)
            return;

        if (args.Show)
            RemComp<HiddenFactionTagComponent>(grid);
        else
            EnsureComp<HiddenFactionTagComponent>(grid);
    }
}
