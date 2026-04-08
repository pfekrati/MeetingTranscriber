using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MeetingTranscriber.Models;
using MeetingTranscriber.Services;
using MeetingTranscriber.Views;

namespace MeetingTranscriber;

public partial class MainWindow : Window
{
    private MeetingOrchestrator? _orchestrator;
    private AppSettings? _settings;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsWindow.LoadOrPrompt();
        if (_settings == null)
        {
            Application.Current.Shutdown();
            return;
        }

        InitializeOrchestrator();
    }

    private void InitializeOrchestrator()
    {
        _orchestrator?.Dispose();
        _orchestrator = new MeetingOrchestrator(_settings!);

        _orchestrator.StatusChanged += status =>
            Dispatcher.Invoke(() => StatusText.Text = status);

        _orchestrator.TranscriptUpdated += transcript =>
            Dispatcher.Invoke(() => LiveTranscriptText.Text = transcript);

        _orchestrator.MeetingCompleted += meeting =>
            Dispatcher.Invoke(() => OnMeetingCompleted(meeting));

        _orchestrator.ErrorOccurred += error =>
            Dispatcher.Invoke(() => StatusText.Text = $"Error: {error}");

        RefreshMeetingsList();
        StatusText.Text = "Ready. Click Start Recording to begin.";
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_orchestrator == null || _orchestrator.IsRecording) return;

        _orchestrator.StartTranscription();
        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        LiveTranscriptPanel.Visibility = Visibility.Visible;
    }

    private void OnMeetingCompleted(Meeting meeting)
    {
        StartButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        LiveTranscriptPanel.Visibility = Visibility.Collapsed;
        LiveTranscriptText.Text = string.Empty;
        RefreshMeetingsList();

        MessageBox.Show(
            $"Meeting transcription complete!\n\nTitle: {meeting.Title}",
            "Meeting Completed",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RefreshMeetingsList()
    {
        if (_orchestrator == null) return;
        var meetings = _orchestrator.Database.GetAllMeetings();
        MeetingsList.ItemsSource = meetings;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _orchestrator?.StopTranscription();
        StartButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow();
        if (win.ShowDialog() == true)
        {
            _settings = win.Settings;
            InitializeOrchestrator();
        }
    }

    private void DeleteMeeting_Click(object sender, RoutedEventArgs e)
    {
        if (MeetingsList.SelectedItem is not Meeting meeting || _orchestrator == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete \"{meeting.Title}\"?",
            "Delete Meeting",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _orchestrator.Database.DeleteMeeting(meeting.Id);
            RefreshMeetingsList();
        }
    }

    private void MeetingsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MeetingsList.SelectedItem is Meeting meeting && _orchestrator != null)
        {
            var detailWin = new MeetingDetailWindow(meeting, _orchestrator.Foundry, _orchestrator.Database);
            if (detailWin.IsDeleted)
                RefreshMeetingsList();
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide(); // Hide to system tray
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // On close, fully shut down
        _orchestrator?.Dispose();
    }
}
