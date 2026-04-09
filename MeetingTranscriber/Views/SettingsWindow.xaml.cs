using System.IO;
using System.Text.Json;
using System.Windows;
using Azure.Core;
using Azure.Identity;
using Microsoft.Win32;
using MeetingTranscriber.Models;
using MeetingTranscriber.Services;

namespace MeetingTranscriber.Views;

public partial class SettingsWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingTranscriber", "settings.json");

    public AppSettings Settings { get; private set; } = new();

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }

        EndpointBox.Text = Settings.FoundryEndpoint;
        ApiKeyBox.Password = Settings.FoundryApiKey;
        EntraAuthCheckBox.IsChecked = Settings.UseEntraAuth;
        WhisperBox.Text = Settings.WhisperDeploymentName;
        ChatBox.Text = Settings.ChatDeploymentName;
        StartWithWindowsCheckBox.IsChecked = Settings.StartWithWindows;
        StartMinimizedCheckBox.IsChecked = Settings.StartMinimized;
        SilenceTimeoutBox.Text = Settings.SilenceTimeoutMinutes.ToString();
        UpdateAuthVisibility();
        LoadSignInStatus();
    }

    private void LoadSignInStatus()
    {
        var record = FoundryService.LoadAuthRecord();
        if (record != null)
        {
            SignInStatus.Text = $"✓ Signed in as {record.Username}";
        }
        else
        {
            SignInStatus.Text = "Not signed in";
        }
    }

    private void EntraAuthCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAuthVisibility();
    }

    private void UpdateAuthVisibility()
    {
        bool useEntra = EntraAuthCheckBox.IsChecked == true;
        SignInPanel.Visibility = useEntra ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyLabel.Visibility = useEntra ? Visibility.Collapsed : Visibility.Visible;
        ApiKeyBox.Visibility = useEntra ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        SignInStatus.Text = "Opening browser for sign-in...";
        try
        {
            var credential = FoundryService.CreateEntraCredential();
            var record = await credential.AuthenticateAsync(
                new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]));
            FoundryService.SaveAuthRecord(record);
            SignInStatus.Text = $"✓ Signed in as {record.Username}";
        }
        catch (Exception ex)
        {
            SignInStatus.Text = $"Sign-in failed: {ex.Message}";
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.FoundryEndpoint = EndpointBox.Text.Trim();
        Settings.FoundryApiKey = ApiKeyBox.Password.Trim();
        Settings.UseEntraAuth = EntraAuthCheckBox.IsChecked == true;
        Settings.WhisperDeploymentName = WhisperBox.Text.Trim();
        Settings.ChatDeploymentName = ChatBox.Text.Trim();
        Settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        Settings.StartMinimized = StartMinimizedCheckBox.IsChecked == true;
        Settings.SilenceTimeoutMinutes = int.TryParse(SilenceTimeoutBox.Text.Trim(), out int timeout) && timeout >= 0 ? timeout : 3;

        if (string.IsNullOrEmpty(Settings.FoundryEndpoint))
        {
            MessageBox.Show("Endpoint URL is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Settings.UseEntraAuth)
        {
            var record = FoundryService.LoadAuthRecord();
            if (record == null)
            {
                MessageBox.Show("Please sign in with Microsoft Entra ID before saving.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (string.IsNullOrEmpty(Settings.FoundryApiKey))
        {
            MessageBox.Show("API Key is required when not using Entra ID authentication.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));

        ApplyStartWithWindows(Settings.StartWithWindows);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "MeetingTranscriber";

    private static void ApplyStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
            if (key == null) return;

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()!.Location;
                key.SetValue(StartupValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
            }
        }
        catch { }
    }

    public static AppSettings LoadOrPrompt()
    {
        AppSettings settings = new();
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }

        if (string.IsNullOrEmpty(settings.FoundryEndpoint)
            || (!settings.UseEntraAuth && string.IsNullOrEmpty(settings.FoundryApiKey)))
        {
            var win = new SettingsWindow();
            if (win.ShowDialog() == true)
                return win.Settings;
            else
                return null!;
        }

        return settings;
    }
}
