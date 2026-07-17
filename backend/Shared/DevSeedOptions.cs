namespace Backend.Shared;

public class DevSeedOptions
{
    public bool Enabled { get; set; }
    public string AdminEmail { get; set; } = "admin@vni.local";
    public string AdminPassword { get; set; } = "Admin@123";
    public Guid DefaultSocialChannelId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid DefaultPageContextId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public Guid DefaultCategoryId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000003");
}
