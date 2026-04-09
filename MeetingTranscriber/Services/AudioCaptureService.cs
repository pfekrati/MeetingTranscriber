using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Services;

/// <summary>
/// Captures system audio output (loopback) and, when the microphone is already
/// in use by another application, also captures microphone input.
/// The two streams are mixed into a single WAV for transcription.
/// </summary>
public class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _loopbackCapture;
    private WasapiCapture? _micCapture;
    private WaveFileWriter? _writer;
    private readonly object _writerLock = new();
    private string? _currentFilePath;
    private readonly string _outputDirectory;

    // For chunked transcription
    private WaveFileWriter? _chunkWriter;
    private string? _currentChunkPath;
    private int _chunkIndex;
    private DateTime _chunkStartTime;
    private readonly TimeSpan _chunkDuration = TimeSpan.FromSeconds(30);
    private readonly object _chunkLock = new();

    // Silence detection
    private DateTime _lastNonSilentTime;
    private bool _silenceEventFired;
    private const float SilenceRmsThreshold = 0.005f;

    // Mic monitoring
    private Timer? _micPollTimer;
    private WaveFormat? _targetFormat;
    private string? _chunkDir;
    private readonly object _micLock = new();

    public event Action<string>? AudioChunkReady;
    public event Action<TimeSpan>? SilenceDetected;
    public TimeSpan SilenceTimeout { get; set; } = TimeSpan.FromMinutes(3);
    public bool IsRecording { get; private set; }

    public AudioCaptureService(string outputDirectory = "recordings")
    {
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(_outputDirectory);
    }

    public string StartRecording()
    {
        if (IsRecording) throw new InvalidOperationException("Already recording.");

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _currentFilePath = Path.Combine(_outputDirectory, $"meeting_{timestamp}.wav");
        _chunkDir = Path.Combine(_outputDirectory, $"chunks_{timestamp}");
        Directory.CreateDirectory(_chunkDir);

        // Target format: 16kHz mono 16-bit (optimal for Whisper)
        _targetFormat = new WaveFormat(16000, 16, 1);

        // Full recording writer
        _writer = new WaveFileWriter(_currentFilePath, _targetFormat);

        // Chunk writer
        _chunkIndex = 0;
        _currentChunkPath = Path.Combine(_chunkDir, $"chunk_{_chunkIndex:D4}.wav");
        _chunkWriter = new WaveFileWriter(_currentChunkPath, _targetFormat);
        _chunkStartTime = DateTime.Now;

        // Setup loopback capture (system audio output)
        _loopbackCapture = new WasapiLoopbackCapture();
        var loopbackResampler = CreateResampler(_loopbackCapture.WaveFormat, _targetFormat);

        _loopbackCapture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded == 0) return;
            byte[] resampled = ResampleBuffer(e.Buffer, e.BytesRecorded, _loopbackCapture.WaveFormat, _targetFormat, loopbackResampler);
            WriteAudioData(resampled, _chunkDir, _targetFormat);
        };

        _lastNonSilentTime = DateTime.Now;
        _silenceEventFired = false;

        _loopbackCapture.StartRecording();
        IsRecording = true;

        // Poll every 2 seconds to attach/detach mic based on whether it is
        // already in use by another application.
        _micPollTimer = new Timer(_ => PollMicrophoneState(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        return _currentFilePath;
    }

    /// <summary>
    /// Checks whether the default microphone is currently in use by another app.
    /// If it is and we haven't attached yet, start capturing from it.
    /// If it is no longer in use and we are capturing, stop.
    /// </summary>
    private void PollMicrophoneState()
    {
        if (!IsRecording) return;

        try
        {
            bool micInUse = IsMicrophoneInUse();

            lock (_micLock)
            {
                if (micInUse && _micCapture == null)
                {
                    StartMicCapture();
                }
                else if (!micInUse && _micCapture != null)
                {
                    StopMicCapture();
                }
            }
        }
        catch
        {
            // Ignore errors during polling — mic capture is best-effort
        }
    }

    private void StartMicCapture()
    {
        try
        {
            var micDevice = GetDefaultMicDevice();
            if (micDevice == null || _targetFormat == null || _chunkDir == null) return;

            _micCapture = new WasapiCapture(micDevice);
            _micCapture.WaveFormat = new WaveFormat(44100, 16, 1);
            var micResampler = CreateResampler(_micCapture.WaveFormat, _targetFormat);
            var fmt = _targetFormat;
            var dir = _chunkDir;

            _micCapture.DataAvailable += (s, e) =>
            {
                if (e.BytesRecorded == 0) return;
                byte[] resampled = ResampleBuffer(e.Buffer, e.BytesRecorded, _micCapture.WaveFormat, fmt, micResampler);
                WriteAudioData(resampled, dir, fmt);
            };

            _micCapture.StartRecording();
        }
        catch
        {
            // If mic capture fails, clean up and continue with loopback only
            _micCapture?.Dispose();
            _micCapture = null;
        }
    }

    private void StopMicCapture()
    {
        try
        {
            _micCapture?.StopRecording();
        }
        catch { }

        _micCapture?.Dispose();
        _micCapture = null;
    }

    /// <summary>
    /// Returns true if the default microphone is currently being used by
    /// another application (i.e. it has an active audio session with audible signal).
    /// </summary>
    private static bool IsMicrophoneInUse()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            // AudioMeterInformation.MasterPeakValue reports the current peak
            // level of the device. A value > 0 means audio is actively flowing
            // through the mic (i.e. it's in use by some application).
            float peak = mic.AudioMeterInformation.MasterPeakValue;
            return peak > 0.0001f;
        }
        catch
        {
            return false;
        }
    }

    private void WriteAudioData(byte[] data, string chunkDir, WaveFormat targetFormat)
    {
        lock (_writerLock)
        {
            _writer?.Write(data, 0, data.Length);
        }

        // Silence detection: compute RMS of the 16-bit PCM buffer
        float rms = ComputeRms(data);
        if (rms > SilenceRmsThreshold)
        {
            _lastNonSilentTime = DateTime.Now;
            _silenceEventFired = false;
        }
        else if (!_silenceEventFired && SilenceTimeout > TimeSpan.Zero)
        {
            var silenceDuration = DateTime.Now - _lastNonSilentTime;
            if (silenceDuration >= SilenceTimeout)
            {
                _silenceEventFired = true;
                SilenceDetected?.Invoke(silenceDuration);
            }
        }

        lock (_chunkLock)
        {
            _chunkWriter?.Write(data, 0, data.Length);

            // Check if chunk duration exceeded
            if (DateTime.Now - _chunkStartTime >= _chunkDuration)
            {
                string completedChunk = _currentChunkPath!;
                _chunkWriter?.Dispose();

                _chunkIndex++;
                _currentChunkPath = Path.Combine(chunkDir, $"chunk_{_chunkIndex:D4}.wav");
                _chunkWriter = new WaveFileWriter(_currentChunkPath, targetFormat);
                _chunkStartTime = DateTime.Now;

                // Notify that a chunk is ready for transcription
                AudioChunkReady?.Invoke(completedChunk);
            }
        }
    }

    public string? StopRecording()
    {
        if (!IsRecording) return null;
        IsRecording = false;

        _micPollTimer?.Dispose();
        _micPollTimer = null;

        _loopbackCapture?.StopRecording();

        lock (_micLock)
        {
            StopMicCapture();
        }

        // Flush final chunk
        lock (_chunkLock)
        {
            if (_chunkWriter != null)
            {
                string finalChunk = _currentChunkPath!;
                _chunkWriter.Dispose();
                _chunkWriter = null;
                AudioChunkReady?.Invoke(finalChunk);
            }
        }

        lock (_writerLock)
        {
            _writer?.Dispose();
            _writer = null;
        }

        _loopbackCapture?.Dispose();
        _loopbackCapture = null;

        return _currentFilePath;
    }

    private static MMDevice? GetDefaultMicDevice()
    {
        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            return deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            return null;
        }
    }

    private static MediaFoundationResampler CreateResampler(WaveFormat sourceFormat, WaveFormat targetFormat)
    {
        // We create a dummy provider; actual resampling happens in ResampleBuffer
        return null!; // Placeholder - we use a simpler approach below
    }

    private static byte[] ResampleBuffer(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat, WaveFormat targetFormat, MediaFoundationResampler? _)
    {
        // Convert source audio to target format using a buffered approach
        using var sourceStream = new RawSourceWaveStream(new MemoryStream(buffer, 0, bytesRecorded), sourceFormat);
        using var resampler = new MediaFoundationResampler(sourceStream, targetFormat);
        resampler.ResamplerQuality = 60;

        using var ms = new MemoryStream();
        byte[] readBuffer = new byte[4096];
        int read;
        while ((read = resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            ms.Write(readBuffer, 0, read);
        }
        return ms.ToArray();
    }

    private static float ComputeRms(byte[] buffer)
    {
        if (buffer.Length < 2) return 0f;
        long sumSquares = 0;
        int sampleCount = buffer.Length / 2;
        for (int i = 0; i < buffer.Length - 1; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSquares += (long)sample * sample;
        }
        double rms = Math.Sqrt((double)sumSquares / sampleCount);
        return (float)(rms / short.MaxValue);
    }

    public void Dispose()
    {
        _micPollTimer?.Dispose();
        _micPollTimer = null;
        if (IsRecording)
            StopRecording();
    }
}

