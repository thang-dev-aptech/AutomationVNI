using Backend.Shared;

namespace Backend.Modules.Crm.Tags;

public enum CrmTagTargetType
{
    PageConversation = 1,
    SocialComment = 2
}

public class CrmTagModel : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Màu hex, ví dụ #FF5722.</summary>
    public string Color { get; set; } = "#607D8B";
}

/// <summary>Gắn một tag với hội thoại tin nhắn hoặc luồng bình luận.</summary>
public class CrmTagLinkModel : BaseEntity
{
    public Guid CrmTagId { get; set; }
    public CrmTagTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
}
