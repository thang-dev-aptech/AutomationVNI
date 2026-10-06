namespace Backend.Modules.ChannelGroup;

public static class ChannelGroupImportModes
{
    public const string Merge = "merge";
    public const string Replace = "replace";

    public static bool IsValid(string? mode)
        => string.Equals(mode, Merge, StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, Replace, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string mode)
        => string.Equals(mode, Replace, StringComparison.OrdinalIgnoreCase) ? Replace : Merge;
}

public class ChannelGroupImportRowError
{
    public int Line { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ChannelGroupImportGroupPreview
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsNew { get; set; }
    public Guid? ExistingGroupId { get; set; }
    public int ChannelsAdded { get; set; }
    public int ChannelsRemoved { get; set; }
    public int ChannelsFinal { get; set; }
}

public class ChannelGroupImportPreviewResponse
{
    public string Mode { get; set; } = ChannelGroupImportModes.Merge;
    public int ValidRowCount { get; set; }
    public List<ChannelGroupImportGroupPreview> Groups { get; set; } = [];
    public List<ChannelGroupImportRowError> Errors { get; set; } = [];
}

public class ChannelGroupImportCommitResponse
{
    public string Mode { get; set; } = ChannelGroupImportModes.Merge;
    public int GroupsCreated { get; set; }
    public int GroupsUpdated { get; set; }
    public int ValidRowCount { get; set; }
    public List<ChannelGroupImportRowError> Errors { get; set; } = [];
    public List<ChannelGroupResponse> Groups { get; set; } = [];
}
