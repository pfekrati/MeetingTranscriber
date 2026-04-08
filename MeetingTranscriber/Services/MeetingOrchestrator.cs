using System.IO;
using System.Text;
using MeetingTranscriber.Models;

namespace MeetingTranscriber.Services;

/// <summary>
/// Orchestrates audio capture, transcription, and summarization.
/// </summary>
public class MeetingOrchestrator : IDisposable
{
    private readonly AudioCaptureService _audioCapture;
    private readonly FoundryService _foundry;
    private readonly DatabaseService _database;
    private readonly StringBuilder _transcriptBuilder = new();
    private Meeting? _currentMeeting;
    private CancellationTokenSource? _cts;

    public event Action<string>? StatusChanged;
    public event Action<Meeting>? MeetingCompleted;
    public event Action<string>? TranscriptUpdated;
    public event Action<string>? ErrorOccurred;

    public Meeting? CurrentMeeting => _currentMeeting;
    public bool IsRecording => _audioCapture.IsRecording;
    public DatabaseService Database => _database;
    public FoundryService Foundry => _foundry;

    public MeetingOrchestrator(AppSettings settings)
    {
        _audioCapture = new AudioCaptureService(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "MeetingTranscriber", "recordings"));
        _foundry = new FoundryService(settings);
        _database = new DatabaseService(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "MeetingTranscriber", settings.DatabasePath));

        Directory.CreateDirectory(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "MeetingTranscriber"));

        _audioCapture.AudioChunkReady += OnAudioChunkReady;
    }

    public void StartTranscription()
    {
        if (_audioCapture.IsRecording) return;

        _cts = new CancellationTokenSource();
        _transcriptBuilder.Clear();
        _currentMeeting = new Meeting { StartTime = DateTime.Now };
        _currentMeeting.Id = _database.CreateMeeting(_currentMeeting);

        _audioCapture.StartRecording();
        StatusChanged?.Invoke("Recording and transcribing meeting...");
    }

    public async void StopTranscription()
    {
        if (!_audioCapture.IsRecording) return;

        StatusChanged?.Invoke("Stopping recording...");
        _audioCapture.StopRecording();
        _cts?.Cancel();

        if (_currentMeeting != null)
        {
            _currentMeeting.EndTime = DateTime.Now;
            _currentMeeting.Transcript = _transcriptBuilder.ToString();

            StatusChanged?.Invoke("Generating summary and title...");
            try
            {
                if (!string.IsNullOrWhiteSpace(_currentMeeting.Transcript))
                {
                    var (title, summary) = await _foundry.SummarizeMeetingAsync(_currentMeeting.Transcript);
                    _currentMeeting.Title = title;
                    _currentMeeting.Summary = summary;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Summarization failed: {ex.Message}");
            }

            _database.UpdateMeeting(_currentMeeting);
            MeetingCompleted?.Invoke(_currentMeeting);
            StatusChanged?.Invoke("Meeting transcription complete.");
        }
    }

    private async void OnAudioChunkReady(string chunkPath)
    {
        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            string text = await _foundry.TranscribeAudioAsync(chunkPath, ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                _transcriptBuilder.AppendLine(text);
                TranscriptUpdated?.Invoke(_transcriptBuilder.ToString());

                // Update DB periodically
                if (_currentMeeting != null)
                {
                    _currentMeeting.Transcript = _transcriptBuilder.ToString();
                    _database.UpdateMeeting(_currentMeeting);
                }
            }

            // Clean up chunk file
            try { File.Delete(chunkPath); } catch { }
        }
        catch (OperationCanceledException)
        {
            // Expected when recording is stopped — not an error
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Transcription chunk error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _audioCapture?.Dispose();
        _foundry?.Dispose();
        _database?.Dispose();
    }
}


