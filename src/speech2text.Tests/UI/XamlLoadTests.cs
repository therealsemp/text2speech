using System;
using System.Windows;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using Moq;
using speech2text.Application;
using speech2text.Domain;
using speech2text.Domain.Ports;
using speech2text.UI.ViewModels;
using speech2text.UI.Views;
using Xunit;

namespace speech2text.Tests.UI;

/// <summary>
/// Instantiates each WPF window to catch XamlParseException (missing StaticResource,
/// bad bindings, etc.) at test time rather than at runtime.
/// Requires STA thread (WPF) and a live Application with MaterialDesign resources.
/// </summary>
public class XamlLoadTests : IClassFixture<WpfApplicationFixture>
{
    [StaFact]
    public void SettingsWindow_XamlLoads()
    {
        var repo = new Mock<ISettingsRepository>();
        repo.Setup(x => x.Load()).Returns(new AppSettings());

        var factory = new Mock<ITranscriptionBackendFactory>();
        factory.Setup(x => x.GetParameterDefinitions(It.IsAny<TranscriptionServiceType>())).Returns([]);

        var vm = new SettingsViewModel(repo.Object, factory.Object);
        _ = new SettingsWindow(vm);
    }

    [StaFact]
    public void OverlayWindow_XamlLoads()
    {
        var repo = new Mock<ISettingsRepository>();
        repo.Setup(x => x.Load()).Returns(new AppSettings());

        var deviceEnumerator = new Mock<IAudioDeviceEnumerator>();
        deviceEnumerator.Setup(x => x.GetDevices()).Returns([]);

        var orchestrator = new RecordingOrchestrator(
            new Mock<IAudioCapture>().Object,
            new Mock<ITranscriptionBackendFactory>().Object,
            new Mock<ITextOutputFactory>().Object,
            repo.Object,
            new Mock<ITranscriptionHistoryRepository>().Object);

        var vm = new OverlayViewModel(orchestrator, repo.Object, deviceEnumerator.Object);
        _ = new OverlayWindow(vm);
    }

    [StaFact]
    public void HistoryWindow_XamlLoads()
    {
        var historyRepository = new Mock<ITranscriptionHistoryRepository>();
        historyRepository.Setup(x => x.GetAll()).Returns([]);

        var vm = new HistoryViewModel(historyRepository.Object);
        _ = new HistoryWindow(vm);
    }
}

/// <summary>
/// Creates the WPF Application instance with MaterialDesign resources once per test session.
/// Without this, StaticResource lookups fail because there is no Application.Current.
/// </summary>
public class WpfApplicationFixture
{
    public WpfApplicationFixture()
    {
        if (System.Windows.Application.Current != null) return;

        // Must run on STA — xUnit.StaFact ensures the fixture is constructed on STA
        // when first used by a [StaFact] test.
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(
            new BundledTheme { BaseTheme = BaseTheme.Dark, PrimaryColor = PrimaryColor.LightBlue, SecondaryColor = SecondaryColor.Red });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")
        });
    }
}
