namespace Content.Server._Mothlight.Economy;

/// <summary>
/// Credits paid to characters who weren't around to receive them, by character name. They're handed over the next
/// time the character is played. Kept on the default map so it's saved with the world.
/// </summary>
[RegisterComponent, Access(typeof(CharacterCreditsSystem))]
public sealed partial class PendingCreditsComponent : Component
{
    [DataField]
    public Dictionary<string, int> Pending = new();
}
