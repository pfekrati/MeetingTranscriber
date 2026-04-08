using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using MeetingTranscriber.Models;

namespace MeetingTranscriber.Services;

/// <summary>
/// Calls Azure AI Foundry endpoints for audio transcription (Whisper)
/// and chat completions (GPT/Phi/Claude) for summarization and Q&amp;A.
/// </summary>
public class FoundryService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly TokenCredential? _credential;
    private AccessToken _cachedToken;
    private bool _useMaxCompletionTokens = true;

    private static readonly string[] TokenScopes = ["https://cognitiveservices.azure.com/.default"];

    private static readonly string AuthRecordPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingTranscriber", "entra_auth_record.json");

    public FoundryService(AppSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        if (_settings.UseEntraAuth)
        {
            _credential = CreateEntraCredential();
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", _settings.FoundryApiKey);
        }
    }

    /// <summary>
    /// Creates an InteractiveBrowserCredential with persistent token cache.
    /// If a saved AuthenticationRecord exists, it is used for silent re-auth.
    /// </summary>
    public static InteractiveBrowserCredential CreateEntraCredential()
    {
        var options = new InteractiveBrowserCredentialOptions
        {
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = "MeetingTranscriber"
            }
        };

        var authRecord = LoadAuthRecord();
        if (authRecord != null)
            options.AuthenticationRecord = authRecord;

        return new InteractiveBrowserCredential(options);
    }

    public static AuthenticationRecord? LoadAuthRecord()
    {
        try
        {
            if (File.Exists(AuthRecordPath))
            {
                using var stream = File.OpenRead(AuthRecordPath);
                return AuthenticationRecord.Deserialize(stream);
            }
        }
        catch { }
        return null;
    }

    public static void SaveAuthRecord(AuthenticationRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AuthRecordPath)!);
        using var stream = File.Create(AuthRecordPath);
        record.Serialize(stream);
    }

    private async Task EnsureAuthHeaderAsync(CancellationToken ct)
    {
        if (_credential == null) return;

        if (_cachedToken.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            _cachedToken = await _credential.GetTokenAsync(
                new TokenRequestContext(TokenScopes), ct);
        }

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _cachedToken.Token);
    }

    /// <summary>
    /// Transcribe an audio file using the Whisper model on Azure AI Foundry.
    /// </summary>
    public async Task<string> TranscribeAudioAsync(string audioFilePath, CancellationToken ct = default)
    {
        await EnsureAuthHeaderAsync(ct);
        string url = $"{_settings.FoundryEndpoint.TrimEnd('/')}/openai/deployments/{_settings.WhisperDeploymentName}/audio/transcriptions?api-version=2024-06-01";

        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(audioFilePath, ct);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", Path.GetFileName(audioFilePath));
        form.Add(new StringContent("whisper-1"), "model");
        form.Add(new StringContent("en"), "language");
        form.Add(new StringContent("verbose_json"), "response_format");

        var response = await _httpClient.PostAsync(url, form, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Transcription failed ({response.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
    }

    /// <summary>
    /// Summarize a meeting transcript and generate a title using the chat model.
    /// </summary>
    public async Task<(string Title, string Summary)> SummarizeMeetingAsync(string transcript, CancellationToken ct = default)
    {
        string systemPrompt = @"You are a meeting assistant. Given a meeting transcript, produce:
1. A short, meaningful meeting title (max 10 words)
2. A well-structured summary with the following sections, each on its own line.
   Use this exact format inside the summary value (use \n for line breaks):

   **Key Discussion Points & Decisions**
   - bullet point per item

   **Key Takeaways**
   - bullet point per item

   **Next Steps**
   - bullet point per item

   **Action Items / Tasks**
   - task description (Owner: name, if mentioned)

Respond in this exact JSON format:
{""title"": ""..."", ""summary"": ""...""}";

        string response = await ChatCompleteAsync(systemPrompt, $"Transcript:\n{transcript}", ct);

        try
        {
            // Try to parse as JSON
            string jsonStr = ExtractJson(response);
            using var doc = JsonDocument.Parse(jsonStr);
            string title = doc.RootElement.GetProperty("title").GetString() ?? "Untitled Meeting";
            string summary = doc.RootElement.GetProperty("summary").GetString() ?? response;
            return (title, summary);
        }
        catch
        {
            return ("Untitled Meeting", response);
        }
    }

    /// <summary>
    /// Answer a question about a meeting using its transcript.
    /// </summary>
    public async Task<string> AskQuestionAsync(string transcript, string question, CancellationToken ct = default)
    {
        string systemPrompt = @"You are a meeting assistant. Answer the user's question based ONLY on the meeting transcript provided below. If the answer cannot be found in the transcript, say so.

Meeting Transcript:
" + transcript;

        return await ChatCompleteAsync(systemPrompt, question, ct);
    }

    private async Task<string> ChatCompleteAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        await EnsureAuthHeaderAsync(ct);
        string url = $"{_settings.FoundryEndpoint.TrimEnd('/')}/openai/deployments/{_settings.ChatDeploymentName}/chat/completions?api-version=2024-06-01";

        var messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userMessage }
        };

        // GPT-5+ uses max_completion_tokens; older models use max_tokens.
        // Try the new parameter first, fall back to old if the model rejects it.
        string tokenParamName = _useMaxCompletionTokens ? "max_completion_tokens" : "max_tokens";
        var payload = new Dictionary<string, object>
        {
            ["messages"] = messages,
            [tokenParamName] = 2000,
            ["temperature"] = 0.3
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // If the model doesn't support the token parameter we tried, switch and retry
            if (json.Contains("max_tokens", StringComparison.OrdinalIgnoreCase)
                || json.Contains("max_completion_tokens", StringComparison.OrdinalIgnoreCase))
            {
                _useMaxCompletionTokens = !_useMaxCompletionTokens;
                tokenParamName = _useMaxCompletionTokens ? "max_completion_tokens" : "max_tokens";
                payload.Remove("max_tokens");
                payload.Remove("max_completion_tokens");
                payload[tokenParamName] = 2000;

                content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                response = await _httpClient.PostAsync(url, content, ct);
                json = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Chat completion failed ({response.StatusCode}): {json}");
            }
            else
            {
                throw new HttpRequestException($"Chat completion failed ({response.StatusCode}): {json}");
            }
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private static string ExtractJson(string text)
    {
        // Extract JSON from possible markdown code blocks
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];
        return text;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

