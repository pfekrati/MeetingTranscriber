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
