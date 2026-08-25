namespace PineAI.Backup;

public class BackupSettings
{
    public string DatabaseName { get; set; } = string.Empty;
    public string LocalTempPath { get; set; } = Path.GetTempPath();
    public int IntervalHours { get; set; } = 1;
    public BaleSettings Bale { get; set; } = new();
}

public class BaleSettings
{
    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
}
