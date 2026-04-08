using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Services;

/// <summary>
/// Captures both system audio output (loopback) and microphone input,
/// mixes them into a single WAV stream, and provides chunks for transcription.
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

    public event Action<string>? AudioChunkReady;
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
        string chunkDir = Path.Combine(_outputDirectory, $"chunks_{timestamp}");
        Directory.CreateDirectory(chunkDir);

        // Target format: 16kHz mono 16-bit (optimal for Whisper)
        var targetFormat = new WaveFormat(16000, 16, 1);

        // Full recording writer
        _writer = new WaveFileWriter(_currentFilePath, targetFormat);

        // Chunk writer
        _chunkIndex = 0;
        _currentChunkPath = Path.Combine(chunkDir, $"chunk_{_chunkIndex:D4}.wav");
        _chunkWriter = new WaveFileWriter(_currentChunkPath, targetFormat);
        _chunkStartTime = DateTime.Now;

        // Setup loopback capture (system audio output)
        _loopbackCapture = new WasapiLoopbackCapture();
        var loopbackResampler = CreateResampler(_loopbackCapture.WaveFormat, targetFormat);

        _loopbackCapture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded == 0) return;
            byte[] resampled = ResampleBuffer(e.Buffer, e.BytesRecorded, _loopbackCapture.WaveFormat, targetFormat, loopbackResampler);
            WriteAudioData(resampled, chunkDir, targetFormat);
        };

        // Setup microphone capture
        try
        {
            var micDevice = GetDefaultMicDevice();
            if (micDevice != null)
            {
                _micCapture = new WasapiCapture(micDevice);
                _micCapture.WaveFormat = new WaveFormat(44100, 16, 1);
                var micResampler = CreateResampler(_micCapture.WaveFormat, targetFormat);

                _micCapture.DataAvailable += (s, e) =>
                {
                    if (e.BytesRecorded == 0) return;
                    byte[] resampled = ResampleBuffer(e.Buffer, e.BytesRecorded, _micCapture.WaveFormat, targetFormat, micResampler);
                    WriteAudioData(resampled, chunkDir, targetFormat);
                };

                _micCapture.StartRecording();
            }
        }
        catch
        {
            // If mic capture fails, continue with loopback only
        }

        _loopbackCapture.StartRecording();
        IsRecording = true;
        return _currentFilePath;
    }

    private void WriteAudioData(byte[] data, string chunkDir, WaveFormat targetFormat)
    {
        lock (_writerLock)
        {
            _writer?.Write(data, 0, data.Length);
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

        _loopbackCapture?.StopRecording();
        _micCapture?.StopRecording();

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
        _micCapture?.Dispose();
        _micCapture = null;

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

    public void Dispose()
    {
        if (IsRecording)
            StopRecording();
    }
}

