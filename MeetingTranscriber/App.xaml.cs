using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using MeetingTranscriber.Models;

namespace MeetingTranscriber;

public partial class App : Application
{
    private TaskbarIcon? _notifyIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create system tray icon using transcript.png
        _notifyIcon = new TaskbarIcon
        {
            ToolTipText = "Meeting Transcriber",
            Icon = LoadIconFromResource("Resources/transcript.png"),
            MenuActivation = PopupActivationMode.RightClick
        };

        // Double-click tray icon to show window
        _notifyIcon.TrayMouseDoubleClick += (s, args) =>
        {
            var mainWindow = Current.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        };

        // Right-click context menu
        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show" };
        showItem.Click += (s, args) =>
        {
            var mw = Current.MainWindow;
            if (mw != null) { mw.Show(); mw.WindowState = WindowState.Normal; mw.Activate(); }
        };
        contextMenu.Items.Add(showItem);

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (s, args) => Current.Shutdown();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenu = contextMenu;

        // If StartMinimized is enabled, hide the main window to system tray on launch
        if (ShouldStartMinimized())
        {
            // MainWindow hasn't been created yet at this point.
            // Hook into Activated to hide it once it first appears.
            EventHandler? handler = null;
            handler = (s, args) =>
            {
                if (Current.MainWindow != null)
                {
                    Current.MainWindow.Activated -= handler;
                    Current.MainWindow.WindowState = WindowState.Minimized;
                    Current.MainWindow.Hide();
                }
            };
            Activated += handler;
        }
    }

    private static bool ShouldStartMinimized()
    {
        try
        {
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MeetingTranscriber", "settings.json");
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings?.StartMinimized == true;
            }
        }
        catch { }
        return false;
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
    }

    private static Icon LoadIconFromResource(string resourcePath)
    {
        var uri = new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute);
        var stream = GetResourceStream(uri)?.Stream;
        if (stream != null)
        {
            using var bitmap = new Bitmap(stream);
            var hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }
        return SystemIcons.Application;
    }
}
