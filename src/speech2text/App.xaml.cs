using System.Windows;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using MessageBox = System.Windows.MessageBox;
using speech2text.Adapters.Audio;
using speech2text.Adapters.History;
using speech2text.Adapters.Hotkey;
using speech2text.Adapters.Settings;
using speech2text.Adapters.TextOutput;
using speech2text.Adapters.Transcription;
using speech2text.Application;
using speech2text.Domain;
using speech2text.Domain.Ports;
using speech2text.UI.ViewModels;
using speech2text.UI.Views;

namespace speech2text;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private NotifyIcon?      _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = BuildServices();

        var overlay    = _services.GetRequiredService<OverlayWindow>();
        var settings   = _services.GetRequiredService<SettingsWindow>();
        var history    = _services.GetRequiredService<HistoryWindow>();
        var orchestrator = _services.GetRequiredService<RecordingOrchestrator>();
        var hotkey     = _services.GetRequiredService<IHotkeyRegistration>();
        var settingsVm = _services.GetRequiredService<SettingsViewModel>();
        var overlayVm  = _services.GetRequiredService<OverlayViewModel>();

        // System tray icon (WinForms NotifyIcon — reliable cross-framework)
        var pngStream = GetResourceStream(new Uri("pack://application:,,,/Resources/icon.png"))!.Stream;
        using var bitmap = new System.Drawing.Bitmap(pngStream);
        var trayGdiIcon = System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Overlay",  null, (_, _) => { overlay.Show();  overlay.Activate(); });
        contextMenu.Items.Add("Open Settings", null, (_, _) => { settings.Show(); settings.Activate(); });
        contextMenu.Items.Add("Open History",  null, (_, _) => { history.Show();  history.Activate(); });
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _trayIcon = new NotifyIcon
        {
            Icon             = trayGdiIcon,
            Text             = "speech2text",
            ContextMenuStrip = contextMenu,
            Visible          = true,
        };
        _trayIcon.DoubleClick += (_, _) => { overlay.Show(); overlay.Activate(); };

        // Minimize to tray / restore from tray
        overlayVm.MinimizeToTrayRequested += () => overlay.Hide();
        overlayVm.ShowOverlayRequested    += () => { overlay.ShowActivated = false; overlay.Show(); overlay.ShowActivated = true; };

        // Open settings window on request from overlay
        overlayVm.OpenSettingsRequested += () => settings.Show();

        // Open history window on request from overlay
        overlayVm.OpenHistoryRequested += () => { history.Show(); history.Activate(); };

        // Re-register hotkey when settings are saved
        settingsVm.SettingsSaved += newSettings =>
        {
            overlayVm.RefreshProfiles(newSettings);
            hotkey.Unregister(newSettings.HotkeyBinding);
            RegisterToggleHotkey(hotkey, orchestrator, newSettings.HotkeyBinding);
        };

        // Register the global toggle hotkey
        var appSettings = _services.GetRequiredService<ISettingsRepository>().Load();
        RegisterToggleHotkey(hotkey, orchestrator, appSettings.HotkeyBinding);

        MainWindow = overlay;
        overlay.Show();
    }

    private static void RegisterToggleHotkey(
        IHotkeyRegistration hotkey,
        RecordingOrchestrator orchestrator,
        string binding)
    {
        try
        {
            hotkey.Register(binding, () =>
            {
                if (orchestrator.State == RecordingState.Idle)
                    _ = orchestrator.StartRecordingAsync();
                else if (orchestrator.State == RecordingState.Recording)
                    orchestrator.StopRecording();
            });
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Hotkey Registration Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Infrastructure — adapters
        services.AddSingleton<IAudioCapture,          NAudioCaptureAdapter>();
        services.AddSingleton<IAudioDeviceEnumerator, NAudioDeviceEnumerator>();
        services.AddSingleton<ITranscriptionBackendFactory, TranscriptionBackendFactory>();
        services.AddSingleton<ITextOutputFactory,     TextOutputFactory>();
        services.AddSingleton<ISettingsRepository,    JsonSettingsRepository>();
        services.AddSingleton<IHotkeyRegistration,    NHotkeyAdapter>();
        services.AddSingleton<ITranscriptionHistoryRepository, InMemoryTranscriptionHistoryRepository>();

        // Application
        services.AddSingleton<RecordingOrchestrator>();

        // UI
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<SettingsWindow>();
        services.AddSingleton<HistoryWindow>();

        return services.BuildServiceProvider();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
