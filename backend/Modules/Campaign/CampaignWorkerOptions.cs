namespace Backend.Modules.Campaign;

public class CampaignWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
}
