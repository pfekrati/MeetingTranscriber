namespace MeetingTranscriber.Models;

public class Meeting
{
    public int Id { get; set; }
    public string Title { get; set; } = "Untitled Meeting";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Transcript { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
