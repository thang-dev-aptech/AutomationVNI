namespace Backend.Modules.SocialComment.Enums;

public enum CommentInboxStatus
{
    New = 1,
    InProgress = 2,
    Replied = 3,
    Ignored = 4,
    Deleted = 5
}

public enum CommentActionType
{
    Reply = 1,
    Hide = 2,
    Unhide = 3,
    Delete = 4,
    ApprovePending = 5,
    IgnorePending = 6,
    Assign = 7,
    SetStatus = 8,
    AddNote = 9
}

public enum WebhookEventStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Skipped = 5
}

public enum SocialCommentSyncMode
{
    Full = 1,
    Recent = 2
}
