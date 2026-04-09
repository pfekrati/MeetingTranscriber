namespace MeetingTranscriber.Models;

public class AppSettings
{
    public string FoundryEndpoint { get; set; } = string.Empty;
    public string FoundryApiKey { get; set; } = string.Empty;
    public bool UseEntraAuth { get; set; } = true;
    public string WhisperDeploymentName { get; set; } = "whisper-large-v3-turbo";
    public string ChatDeploymentName { get; set; } = "gpt-4o-mini";
    public string DatabasePath { get; set; } = "meetings.db";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public int SilenceTimeoutMinutes { get; set; } = 3;
}
