namespace Backend.Modules.Crm.Reminders;

/// <summary>
/// Cấu hình CrmReminderWorker. Mặc định tắt — không quét DB, không tạo AppNotification CrmReminderDue.
/// </summary>
public class CrmReminderWorkerOptions
{
    public bool Enabled { get; set; } = false;
    public int IntervalSeconds { get; set; } = 60;
}
