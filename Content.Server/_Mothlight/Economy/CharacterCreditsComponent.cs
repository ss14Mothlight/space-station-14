namespace Content.Server._Mothlight.Economy;

/// <summary>
/// A character's credits. Kept on the character's body so they're saved with the character, instead of on the
/// player's account like on Starlight.
/// </summary>
[RegisterComponent, Access(typeof(CharacterCreditsSystem))]
public sealed partial class CharacterCreditsComponent : Component
{
    [DataField]
    public int Balance;
}
