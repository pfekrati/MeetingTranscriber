using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using MeetingTranscriber.Models;

namespace MeetingTranscriber;

public partial class App : Application
{
    private TaskbarIcon? _notifyIcon;

    /// <summary>True when the app was launched with the --minimized flag (e.g. from the Run registry key).</summary>
    public static bool LaunchedMinimized { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log unhandled exceptions so startup crashes are diagnosable.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        LaunchedMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        // Create system tray icon with retry – the shell notification area may
        // not be ready immediately when the app is launched during Windows logon.
        _notifyIcon = CreateNotifyIconWithRetry(maxAttempts: 5, delayMs: 1000);

        // Left-click (or double-click) tray icon to show window.
        // Commands are more reliable than routed events when the TaskbarIcon
        // is created in code-behind (not in the XAML visual tree).
        var showCommand = new RelayCommand(ShowMainWindow);
        _notifyIcon.LeftClickCommand = showCommand;
        _notifyIcon.DoubleClickCommand = showCommand;
        _notifyIcon.NoLeftClickDelay = true;

        // Right-click context menu
        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show" };
        showItem.Click += (s, args) =>
        {
            var mw = Current.MainWindow;
            if (mw != null) { mw.Show(); mw.WindowState = WindowState.Normal; mw.Activate(); }
        };
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (s, args) =>
        {
            if (Current.MainWindow is MainWindow mw)
                mw.Quit();
            else
                Current.Shutdown();
        };
        contextMenu.Items.Add(quitItem);

        _notifyIcon.ContextMenu = contextMenu;

        // If StartMinimized is enabled (or launched with --minimized), hide
        // the main window to the system tray on launch.
        if (LaunchedMinimized || ShouldStartMinimized())
        {
            // MainWindow hasn't been created yet at this point.
            // Hook into Activated to hide it once it first appears.
            EventHandler? handler = null;
            handler = (s, args) =>
            {
                // One-shot: remove from the same event we subscribed to
                // (Application.Activated) so it never fires again. Previously this
                // unsubscribed from MainWindow.Activated by mistake, so the handler
                // kept running and re-hid the window every time the app was
                // activated (e.g. when double-clicking the tray icon).
                Activated -= handler;

                if (Current.MainWindow != null)
                {
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

    private static void ShowMainWindow()
    {
        var mainWindow = Current.MainWindow;
        if (mainWindow != null)
        {
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }
    }

    /// <summary>Minimal ICommand implementation for tray icon commands.</summary>
    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }

    private TaskbarIcon CreateNotifyIconWithRetry(int maxAttempts, int delayMs)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return new TaskbarIcon
                {
                    ToolTipText = "Meeting Transcriber",
                    Icon = LoadIconFromResource("Resources/transcript.png"),
                    MenuActivation = PopupActivationMode.RightClick
                };
            }
            catch when (attempt < maxAttempts)
            {
                Thread.Sleep(delayMs);
            }
        }

        // Final attempt – let exception propagate if it still fails.
        return new TaskbarIcon
        {
            ToolTipText = "Meeting Transcriber",
            Icon = LoadIconFromResource("Resources/transcript.png"),
            MenuActivation = PopupActivationMode.RightClick
        };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MeetingTranscriber");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "crash.log");
            string entry = $"[{DateTime.Now:O}] {e.Exception}\n";
            File.AppendAllText(logPath, entry);
        }
        catch { }
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
