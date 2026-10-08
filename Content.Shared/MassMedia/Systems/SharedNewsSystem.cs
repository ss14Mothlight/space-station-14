using Robust.Shared.Serialization;

namespace Content.Shared.MassMedia.Systems;

public abstract class SharedNewsSystem : EntitySystem
{
    public const int MaxTitleLength = 35; // Starlight, slight increase
    public const int MaxContentLength = 3072; // Starlight, +50% increase
}

[DataDefinition, Serializable, NetSerializable]
public partial struct NewsArticle
{
    [DataField]
    public string Title;

    [DataField]
    public string Content;

    [DataField]
    public string? Author;

    [ViewVariables]
    public ICollection<(NetEntity, uint)>? AuthorStationRecordKeyIds;

    [DataField]
    public TimeSpan ShareTime;
    // Starlight-edit: start
    [ViewVariables(VVAccess.ReadWrite)]
    public int Likes;

    [ViewVariables(VVAccess.ReadWrite)]
    public int Dislikes;

    [ViewVariables(VVAccess.ReadWrite)]
    public int Views;
    // Starlight-edit: end
}

[ByRefEvent]
public record struct NewsArticlePublishedEvent(NewsArticle Article);

[ByRefEvent]
public record struct NewsArticleDeletedEvent;
