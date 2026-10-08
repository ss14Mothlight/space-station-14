using Content.Shared.Silicons.Bots;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Silicons.Bots;

/// <summary>
/// This marker component indicates that its entity has been recently hugged by a HugBot and should not be hugged again
/// before <see cref="CooldownCompleteAfter">a cooldown period</see> in order to prevent hug spam.
/// </summary>
/// <see cref="SharedHugBotSystem"/>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RecentlyHuggedByHugBotComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan CooldownCompleteAfter = TimeSpan.MinValue;
}
