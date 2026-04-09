# Meeting Transcriber

A Windows desktop app that records audio, transcribes speech in real time, and generates AI-powered summaries — all running locally on your machine with [Azure AI Foundry](https://ai.azure.com/) as the AI backend. Works with any meeting or conversation — not tied to any specific conferencing platform.

## Features

- **Real-time transcription** — Captures both system audio (loopback) and microphone input, mixes them into 30-second WAV chunks, and streams them to Azure OpenAI Whisper for live transcription.
- **AI-generated summaries** — When a recording ends, the full transcript is sent to a chat model (e.g. GPT-4o-mini) to produce a title, key discussion points, takeaways, next steps, and action items.
- **Ask questions about meetings** — Open any past meeting and ask natural-language questions about its content; the AI answers based solely on the transcript.
- **Meeting history** — All meetings are stored in a local SQLite database with full transcript and summary. Double-click to review; right-click to delete.
- **System tray integration** — Minimizes to the Windows system tray so it stays out of the way during meetings.
- **Authentication options** — Supports both Microsoft Entra ID (interactive browser sign-in with persistent token cache) and API key authentication for Azure AI Foundry.

## Architecture

```
┌──────────────────────┐     ┌──────────────────────┐
│  MeetingOrchestrator  │────▶│    FoundryService    │
│  (coordinates flow)   │     │  (Whisper + Chat AI) │
└──────────┬───────────┘     └──────────────────────┘
           │
┌──────────┼──────────────┐
▼          ▼               ▼
┌─────────────┐ ┌───────────┐ ┌──────────────┐
│ AudioCapture │ │ Database  │ │   WPF UI     │
│   Service    │ │  Service  │ │ (MainWindow) │
└─────────────┘ └───────────┘ └──────────────┘
```

| Component | Responsibility |
|---|---|
| **AudioCaptureService** | Records system audio (WASAPI loopback) + microphone, resamples to 16 kHz mono, emits 30 s chunks |
| **FoundryService** | Calls Azure AI Foundry — Whisper for transcription, chat model for summaries and Q&A |
| **DatabaseService** | Stores meetings (title, transcript, summary, timestamps) in a local SQLite database |
| **MeetingOrchestrator** | Wires everything together: recording → transcription → summarization → storage |

## Prerequisites

- **Windows 10/11** — This is a WPF app that uses Windows-specific audio APIs (WASAPI).
- [**.NET 10 SDK**](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- **Azure AI Foundry** resource with the following model deployments:
  - A **Whisper** deployment for audio transcription
  - A **Chat** deployment for summarization and Q&A

## Getting Started

### Option A: Download the installer

Download the latest signed `MeetingTranscriber-x.x.x-win-x64-setup.exe` from the [Releases](https://github.com/pfekrati/MeetingTranscriber/releases) page and run it.

### Option B: Build from source

```bash
git clone https://github.com/pfekrati/MeetingTranscriber.git
cd MeetingTranscriber
dotnet build
dotnet run --project MeetingTranscriber
```

### Building the installer yourself

```powershell
# Unsigned (for local testing)
.\build\Build-Installer.ps1

# Signed using the default PFX in .\certs\ and password from .\certs\password.txt
.\build\Build-Installer.ps1 -Sign

# Signed with a specific PFX certificate
.\build\Build-Installer.ps1 -Sign -CertificatePath ".\certs\MeetingTranscriber-CodeSigning.pfx" -CertificatePassword "password"
```

This produces a self-contained single-file `MeetingTranscriber.exe` (~75 MB) and a Windows installer in the `artifacts/` folder. No .NET runtime installation is required on the target machine.

Installer build prerequisites:

- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`ISCC.exe`)
- Optional signing: Windows SDK `signtool.exe`

### Configure on first launch

The Settings window opens automatically on first run. You'll need to provide:

| Setting | Description |
|---|---|
| **Azure AI Foundry Endpoint** | Your resource's endpoint URL (e.g. `https://my-resource.openai.azure.com/`) |
| **Authentication** | Choose **Microsoft Entra ID** (recommended) or **API Key** |
| **Whisper Deployment Name** | Name of your Whisper model deployment |
| **Chat Deployment Name** | Name of your chat model deployment |

Settings are saved to `%LOCALAPPDATA%\MeetingTranscriber\settings.json`.

### 4. Using the app

1. **Start the app** — it sits in the system tray, ready to record.
2. **Click Start Recording** — when your meeting or conversation begins.
3. **Watch the live transcript** — transcription appears in real time as audio chunks are processed.
4. **Click Stop Recording** — when finished. The app generates an AI summary with title, key points, and action items.
5. **Review past meetings** — double-click any meeting in the history list to view its transcript and summary, or ask follow-up questions.

## Data Storage

All data is stored locally on your machine:

| Data | Location |
|---|---|
| Settings | `%LOCALAPPDATA%\MeetingTranscriber\settings.json` |
| Meeting database | `%LOCALAPPDATA%\MeetingTranscriber\meetings.db` |
| Audio recordings | `%LOCALAPPDATA%\MeetingTranscriber\recordings\` |
| Entra auth token cache | `%LOCALAPPDATA%\MeetingTranscriber\entra_auth_record.json` |

> **Note**: Audio chunks are deleted after transcription. The full recording WAV file is kept in the recordings folder.

## Tech Stack

- **UI**: WPF (.NET 10, Windows)
- **Audio**: [NAudio](https://github.com/naudio/NAudio) (WASAPI loopback + microphone capture)
- **AI**: [Azure AI Foundry](https://ai.azure.com/) (OpenAI Whisper + chat completions)
- **Auth**: [Azure.Identity](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/identity/Azure.Identity) (Entra ID with persistent token cache)
- **Database**: [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) (local SQLite)
- **System tray**: [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon)

## Contributing

Contributions are welcome! Please open an issue to discuss your idea before submitting a pull request.

1. Fork the repository
2. Create a feature branch (`git checkout -b my-feature`)
3. Commit your changes (`git commit -m 'Add my feature'`)
4. Push to the branch (`git push origin my-feature`)
5. Open a Pull Request

## License

This project is licensed under the [MIT License](LICENSE).
