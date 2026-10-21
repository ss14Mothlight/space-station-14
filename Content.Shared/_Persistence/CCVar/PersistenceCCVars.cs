using Robust.Shared.Configuration;

namespace Content.Shared._Persistence.CCVar;

/// <summary>
/// CVars for content ported from SS14-Persistence.
/// </summary>
[CVarDefs]
public sealed class PersistenceCCVars
{
    /// <summary>
    /// The most tiles a grid can have to be parked in bluespace.
    /// </summary>
    public static readonly CVarDef<int> BluespaceParkingMaxTiles =
        CVarDef.Create("bsp.max_tiles", 800, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// How long parking a grid takes, in seconds.
    /// </summary>
    public static readonly CVarDef<int> BluespaceParkingParkDelay =
        CVarDef.Create("bsp.park_delay", 25, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// How long unparking a grid takes, in seconds.
    /// </summary>
    public static readonly CVarDef<int> BluespaceParkingUnparkDelay =
        CVarDef.Create("bsp.unpark_delay", 25, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// How close to where a grid was parked its anchor key has to be to unpark it.
    /// </summary>
    public static readonly CVarDef<float> BluespaceUnparkMaxDistance =
        CVarDef.Create("bsp.unpark_max_distance", 100f, CVar.ARCHIVE | CVar.REPLICATED);
}
