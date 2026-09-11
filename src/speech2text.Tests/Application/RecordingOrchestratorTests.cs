using Moq;
using speech2text.Application;
using speech2text.Domain;
using speech2text.Domain.Ports;

namespace speech2text.Tests.Application;

public class RecordingOrchestratorTests
{
    private readonly Mock<IAudioCapture> _audioCapture = new();
    private readonly Mock<ITranscriptionBackend> _backend = new();
    private readonly Mock<ITranscriptionBackendFactory> _backendFactory = new();
    private readonly Mock<ITextOutput> _textOutput = new();
    private readonly Mock<ITextOutputFactory> _textOutputFactory = new();
    private readonly Mock<ISettingsRepository> _settingsRepository = new();
    private readonly Mock<ITranscriptionHistoryRepository> _historyRepository = new();
    private readonly RecordingOrchestrator _orchestrator;

    private static readonly byte[] SampleAudio = [1, 2, 3];

    public RecordingOrchestratorTests()
    {
        var profile = new TranscriptionProfile { Id = Guid.NewGuid(), Language = "en-US" };
        var settings = new AppSettings { ActiveProfileId = profile.Id, Profiles = [profile] };

        _settingsRepository.Setup(x => x.Load()).Returns(settings);
        _backendFactory.Setup(x => x.Create(It.IsAny<TranscriptionProfile>())).Returns(_backend.Object);
        _backend.Setup(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcribed text");
        _textOutputFactory.Setup(x => x.Create(It.IsAny<TextInsertionMode>())).Returns(_textOutput.Object);

        _orchestrator = new RecordingOrchestrator(
            _audioCapture.Object,
            _backendFactory.Object,
            _textOutputFactory.Object,
            _settingsRepository.Object,
            _historyRepository.Object);
    }

    // --- Flux normal ---

    [Fact]
    public async Task NormalFlow_TranscribesAndInjectsText()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask;

        _textOutput.Verify(x => x.InjectTextAsync("transcribed text"), Times.Once);
        Assert.Equal(RecordingState.Idle, _orchestrator.State);
    }

    [Fact]
    public async Task NormalFlow_StateIsRecordingWhileAudioCaptures()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);

        var sessionTask = _orchestrator.StartRecordingAsync();

        Assert.Equal(RecordingState.Recording, _orchestrator.State);

        audioTcs.SetResult(SampleAudio);
        await sessionTask;
    }

    [Fact]
    public async Task NormalFlow_AddsTranscriptionToHistory_BeforeInjectingText()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);

        var callOrder = new List<string>();
        _historyRepository.Setup(x => x.Add(It.IsAny<TranscriptionHistoryEntry>()))
            .Callback(() => callOrder.Add("history"));
        _textOutput.Setup(x => x.InjectTextAsync(It.IsAny<string>()))
            .Callback(() => callOrder.Add("inject"))
            .Returns(Task.CompletedTask);

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask;

        _historyRepository.Verify(x => x.Add(It.Is<TranscriptionHistoryEntry>(e => e.Text == "transcribed text")), Times.Once);
        Assert.Equal(["history", "inject"], callOrder);
    }

    [Fact]
    public async Task NormalFlow_PassesAudioAndLanguageToBackend()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask;

        _backend.Verify(x => x.TranscribeAsync(SampleAudio, "en-US", It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Flux cancel (Escape) ---

    [Fact]
    public async Task CancelFlow_DoesNotTranscribeOrInjectText()
    {
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return [];
            });

        var sessionTask = _orchestrator.StartRecordingAsync();
        _orchestrator.CancelRecording();
        await sessionTask;

        _backend.Verify(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _textOutput.Verify(x => x.InjectTextAsync(It.IsAny<string>()), Times.Never);
        Assert.Equal(RecordingState.Idle, _orchestrator.State);
    }

    // --- Erreur de transcription ---

    [Fact]
    public async Task TranscriptionError_DoesNotThrow_ReturnsToIdle()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);
        _backend.Setup(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask; // ne doit pas throw

        Assert.Equal(RecordingState.Idle, _orchestrator.State);
    }

    [Fact]
    public async Task TranscriptionError_DoesNotInjectText()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);
        _backend.Setup(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask;

        _textOutput.Verify(x => x.InjectTextAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TranscriptionError_DoesNotAddToHistory()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);
        _backend.Setup(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask;

        _historyRepository.Verify(x => x.Add(It.IsAny<TranscriptionHistoryEntry>()), Times.Never);
    }

    [Fact]
    public async Task TranscriptionError_FiresErrorOccurred()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);
        _backend.Setup(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Transcription failed: network error."));

        string? capturedError = null;
        _orchestrator.ErrorOccurred += msg => capturedError = msg;

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask;

        Assert.Equal("Transcription failed: network error.", capturedError);
    }

    // --- Sélection de profil ---

    [Fact]
    public async Task NoProfiles_FiresErrorAndReturnsToIdle()
    {
        var settingsRepo = new Mock<ISettingsRepository>();
        settingsRepo.Setup(x => x.Load()).Returns(new AppSettings { Profiles = [] });

        var audioCapture = new Mock<IAudioCapture>();
        audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAudio);

        var orchestrator = new RecordingOrchestrator(
            audioCapture.Object,
            new Mock<ITranscriptionBackendFactory>().Object,
            new Mock<ITextOutputFactory>().Object,
            settingsRepo.Object,
            new Mock<ITranscriptionHistoryRepository>().Object);

        string? capturedError = null;
        orchestrator.ErrorOccurred += msg => capturedError = msg;

        await orchestrator.StartRecordingAsync();

        Assert.Equal(RecordingState.Idle, orchestrator.State);
        Assert.NotNull(capturedError);
        Assert.Contains("No transcription profile", capturedError);
    }

    [Fact]
    public async Task ActiveProfileIdNotFound_FallsBackToFirstProfile()
    {
        var profile = new TranscriptionProfile { Id = Guid.NewGuid(), Language = "fr" };
        var settings = new AppSettings
        {
            ActiveProfileId = Guid.NewGuid(), // ID qui ne correspond à aucun profil
            Profiles        = [profile]
        };

        var settingsRepo = new Mock<ISettingsRepository>();
        settingsRepo.Setup(x => x.Load()).Returns(settings);

        var backend = new Mock<ITranscriptionBackend>();
        backend.Setup(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var backendFactory = new Mock<ITranscriptionBackendFactory>();
        backendFactory.Setup(x => x.Create(It.IsAny<TranscriptionProfile>())).Returns(backend.Object);

        var audioCapture = new Mock<IAudioCapture>();
        audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAudio);

        var orchestrator = new RecordingOrchestrator(
            audioCapture.Object,
            backendFactory.Object,
            new Mock<ITextOutputFactory>().Object,
            settingsRepo.Object,
            new Mock<ITranscriptionHistoryRepository>().Object);

        await orchestrator.StartRecordingAsync();

        // Doit avoir utilisé le premier profil disponible (language "fr")
        backend.Verify(x => x.TranscribeAsync(It.IsAny<byte[]>(), "fr", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(RecordingState.Idle, orchestrator.State);
    }

    // --- Erreur de création du backend (ex: profil incomplet) ---

    [Fact]
    public async Task BackendCreationError_DoesNotThrow_ReturnsToIdle()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);
        _backendFactory.Setup(x => x.Create(It.IsAny<TranscriptionProfile>()))
            .Throws(new InvalidOperationException("Transcription profile is missing the Deployment Name."));

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask; // ne doit pas throw

        Assert.Equal(RecordingState.Idle, _orchestrator.State);
    }

    [Fact]
    public async Task BackendCreationError_FiresErrorOccurred()
    {
        var audioTcs = new TaskCompletionSource<byte[]>();
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).Returns(audioTcs.Task);
        _backendFactory.Setup(x => x.Create(It.IsAny<TranscriptionProfile>()))
            .Throws(new InvalidOperationException("Transcription profile is missing the Deployment Name."));

        string? capturedError = null;
        _orchestrator.ErrorOccurred += msg => capturedError = msg;

        var sessionTask = _orchestrator.StartRecordingAsync();
        audioTcs.SetResult(SampleAudio);
        await sessionTask;

        Assert.NotNull(capturedError);
        Assert.Contains("Deployment Name", capturedError);
    }

    // --- Erreur de capture audio ---

    [Fact]
    public async Task AudioCaptureError_DoesNotThrow_ReturnsToIdle()
    {
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Audio device unavailable."));

        await _orchestrator.StartRecordingAsync(); // ne doit pas throw

        Assert.Equal(RecordingState.Idle, _orchestrator.State);
    }

    [Fact]
    public async Task AudioCaptureError_FiresErrorOccurred_AndDoesNotTranscribe()
    {
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Audio device unavailable."));

        string? capturedError = null;
        _orchestrator.ErrorOccurred += msg => capturedError = msg;

        await _orchestrator.StartRecordingAsync();

        Assert.Equal("Audio device unavailable.", capturedError);
        _backend.Verify(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Mode d'insertion de texte ---

    [Fact]
    public async Task NormalFlow_UsesTextInsertionModeFromSettings()
    {
        var profile = new TranscriptionProfile { Id = Guid.NewGuid(), Language = "en-US" };
        var settings = new AppSettings
        {
            ActiveProfileId   = profile.Id,
            Profiles          = [profile],
            TextInsertionMode = TextInsertionMode.ClipboardPaste
        };

        var settingsRepo = new Mock<ISettingsRepository>();
        settingsRepo.Setup(x => x.Load()).Returns(settings);

        var backend = new Mock<ITranscriptionBackend>();
        backend.Setup(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var backendFactory = new Mock<ITranscriptionBackendFactory>();
        backendFactory.Setup(x => x.Create(It.IsAny<TranscriptionProfile>())).Returns(backend.Object);

        var textOutputFactory = new Mock<ITextOutputFactory>();

        var audioCapture = new Mock<IAudioCapture>();
        audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>())).ReturnsAsync(SampleAudio);

        var orchestrator = new RecordingOrchestrator(
            audioCapture.Object,
            backendFactory.Object,
            textOutputFactory.Object,
            settingsRepo.Object,
            new Mock<ITranscriptionHistoryRepository>().Object);

        await orchestrator.StartRecordingAsync();

        textOutputFactory.Verify(x => x.Create(TextInsertionMode.ClipboardPaste), Times.Once);
    }

    // --- Micro muet / buffer vide ---

    [Fact]
    public async Task EmptyAudio_FiresMicrophoneError_DoesNotTranscribe()
    {
        _audioCapture.Setup(x => x.RecordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        string? capturedError = null;
        _orchestrator.ErrorOccurred += msg => capturedError = msg;

        await _orchestrator.StartRecordingAsync();

        Assert.NotNull(capturedError);
        Assert.Contains("muted or disconnected", capturedError);
        _backend.Verify(x => x.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(RecordingState.Idle, _orchestrator.State);
    }
}
