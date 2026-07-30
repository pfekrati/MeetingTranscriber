using System.Windows;
using System.Windows.Input;
using MeetingTranscriber.Models;
using MeetingTranscriber.Services;

namespace MeetingTranscriber.Views;

public partial class MeetingDetailWindow : Window
{
    private readonly Meeting _meeting;
    private readonly FoundryService _foundry;
    private readonly DatabaseService? _database;

    public bool IsDeleted { get; private set; }
    public bool IsRenamed { get; private set; }

    public MeetingDetailWindow(Meeting meeting, FoundryService foundry, DatabaseService? database = null)
    {
        InitializeComponent();
        _meeting = meeting;
        _foundry = foundry;
        _database = database;

        TitleText.Text = meeting.Title;
        TimeText.Text = $"{meeting.StartTime:g} — {meeting.EndTime?.ToString("g") ?? "In Progress"}";
        SummaryText.Text = string.IsNullOrWhiteSpace(meeting.Summary) ? "No summary available." : meeting.Summary;
        TranscriptText.Text = string.IsNullOrWhiteSpace(meeting.Transcript) ? "No transcript available." : meeting.Transcript;

        ShowDialog();
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RenameDialog(_meeting.Title);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            _meeting.Title = dialog.MeetingName;
            _database?.UpdateMeeting(_meeting);
            TitleText.Text = _meeting.Title;
            IsRenamed = true;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Meeting",
            FileName = SanitizeFileName(_meeting.Title),
            DefaultExt = ".txt",
            Filter = "Text file (*.txt)|*.txt"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var content = new System.Text.StringBuilder();
            content.AppendLine(_meeting.Title);
            content.AppendLine($"{_meeting.StartTime:g} — {_meeting.EndTime?.ToString("g") ?? "In Progress"}");
            content.AppendLine();
            content.AppendLine("=== Summary ===");
            content.AppendLine(string.IsNullOrWhiteSpace(_meeting.Summary) ? "No summary available." : _meeting.Summary);
            content.AppendLine();
            content.AppendLine("=== Transcript ===");
            content.AppendLine(string.IsNullOrWhiteSpace(_meeting.Transcript) ? "No transcript available." : _meeting.Transcript);

            System.IO.File.WriteAllText(dialog.FileName, content.ToString());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Meeting";
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Are you sure you want to delete \"{_meeting.Title}\"?",
            "Delete Meeting",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _database?.DeleteMeeting(_meeting.Id);
            IsDeleted = true;
            Close();
        }
    }

    private async void Ask_Click(object sender, RoutedEventArgs e)
    {
        await AskQuestion();
    }

    private async void QuestionBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await AskQuestion();
    }

    private async Task AskQuestion()
    {
        string question = QuestionBox.Text.Trim();
        if (string.IsNullOrEmpty(question)) return;

        if (string.IsNullOrWhiteSpace(_meeting.Transcript))
        {
            AnswerText.Text = "No transcript available to answer questions.";
            return;
        }

        AnswerText.Text = "Thinking...";
        try
        {
            string answer = await _foundry.AskQuestionAsync(_meeting.Transcript, question);
            AnswerText.Text = answer;
        }
        catch (Exception ex)
        {
            AnswerText.Text = $"Error: {ex.Message}";
        }
    }
}
