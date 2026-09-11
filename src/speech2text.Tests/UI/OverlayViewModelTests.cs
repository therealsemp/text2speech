using Moq;
using speech2text.Application;
using speech2text.Domain;
using speech2text.Domain.Ports;
using speech2text.UI.ViewModels;

namespace speech2text.Tests.UI;

public class OverlayViewModelTests
{
    /// <summary>
    /// Subclass that overrides Dispatch to run actions synchronously,
    /// avoiding the WPF Application.Current.Dispatcher dependency in tests.
    /// </summary>
    private sealed class TestableOverlayViewModel(
        RecordingOrchestrator orchestrator,
        ISettingsRepository settingsRepository,
        IAudioDeviceEnumerator deviceEnumerator)
        : OverlayViewModel(orchestrator, settingsRepository, deviceEnumerator)
    {
        protected override void Dispatch(Action action) => action();
    }

    private readonly Mock<IAudioCapture> _audioCapture = new();
    private readonly Mock<ITranscriptionBackend> _backend = new();
    private readonly Mock<ITranscriptionBackendFactory> _backendFactory = new();
    private readonly Mock<ITextOutput> _textOutput = new();
    private readonly Mock<ITextOutputFactory> _textOutputFactory = new();
    private readonly Mock<ISettingsRepository> _settingsRepository = new();
    private readonly Mock<IAudioDeviceEnumerator> _deviceEnumerator = new();
    private readonly Mock<ITranscriptionHistoryRepository> _historyRepository = new();
    private readonly RecordingOrchestrator _orchestrator;
    private readonly TestableOverlayViewModel _vm;

    public OverlayViewModelTests()
    {
        var profile  = new TranscriptionProfile { Id = Guid.NewGuid(), Language = "en-US" };
        var settings = new AppSettings { ActiveProfileId = profile.Id, Profiles = [profile] };

        _settingsRepository.Setup(x => x.Load()).Returns(settings);
        _backendFactory.Setup(x => x.Create(It.IsAny<TranscriptionProfile>())).Returns(_backend.Object);
        _deviceEnumerator.Setup(x => x.GetDevices()).Returns([]);
        _textOutputFactory.Setup(x => x.Create(It.IsAny<TextInsertionMode>())).Returns(_textOutput.Object);

        _orchestrator = new RecordingOrchestrator(
            _audioCapture.Object,
            _backendFactory.Object,
            _textOutputFactory.Object,
            _settingsRepository.Object,
            _historyRepository.Object);

        _vm = new TestableOverlayViewModel(_orchestrator, _settingsRepository.Object, _deviceEnumerator.Object);
    }

    // --- Escape key behavior ---

    [Fact]
    public void HandleEscapeKey_WhenIdle_RaisesMinimizeToTrayRequested()
    {
        bool raised = false;
        _vm.MinimizeToTrayRequested += () => raised = true;

        _vm.HandleEscapeKey();

        Assert.True(raised);
    }

    [Fact]
    public async Task HandleEscapeKey_WhenRecording_CancelsRecording_DoesNotMinimize()
    {
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return [];
            });

        bool minimizeRaised = false;
        _vm.MinimizeToTrayRequested += () => minimizeRaised = true;

        var sessionTask = _orchestrator.StartRecordingAsync();
        Assert.Equal(RecordingState.Recording, _orchestrator.State);

        _vm.HandleEscapeKey();
        await sessionTask;

        Assert.False(minimizeRaised);
        Assert.Equal(RecordingState.Idle, _orchestrator.State);
    }

    [Fact]
    public async Task HandleEscapeKey_WhenTranscribing_RaisesMinimizeToTrayRequested()
    {
        var transcribeTcs = new TaskCompletionSource<string>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);
        _backend.Setup(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(transcribeTcs.Task);

        bool minimizeRaised = false;
        _vm.MinimizeToTrayRequested += () => minimizeRaised = true;

        // RecordAsync completes synchronously → orchestrator reaches Transcribing before returning
        var sessionTask = _orchestrator.StartRecordingAsync();
        Assert.Equal(RecordingState.Transcribing, _orchestrator.State);

        _vm.HandleEscapeKey();

        Assert.True(minimizeRaised);

        // Clean up
        transcribeTcs.SetResult("text");
        await sessionTask;
    }

    // --- Show overlay on recording start ---

    [Fact]
    public async Task StartRecording_RaisesShowOverlayRequested()
    {
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return [];
            });

        bool showRaised = false;
        _vm.ShowOverlayRequested += () => showRaised = true;

        var sessionTask = _orchestrator.StartRecordingAsync();
        Assert.True(showRaised);

        // Clean up
        _orchestrator.CancelRecording();
        await sessionTask;
    }
}
