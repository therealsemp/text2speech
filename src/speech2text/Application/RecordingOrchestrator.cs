using speech2text.Domain;
using speech2text.Domain.Ports;

namespace speech2text.Application;

public class RecordingOrchestrator(
    IAudioCapture audioCapture,
    ITranscriptionBackendFactory backendFactory,
    ITextOutputFactory textOutputFactory,
    ISettingsRepository settingsRepository,
    ITranscriptionHistoryRepository historyRepository)
{
    private readonly RecordingSession _session = new();
    private CancellationTokenSource? _cts;
    private bool _cancelled;

    public RecordingState State => _session.State;

    /// <summary>Fired on the calling thread after each state transition.</summary>
    public event Action<RecordingState>? StateChanged;

    /// <summary>Fired when a non-cancellation error occurs during recording or transcription.</summary>
    public event Action<string>? ErrorOccurred;

    private void NotifyStateChanged() => StateChanged?.Invoke(_session.State);

    public async Task StartRecordingAsync()
    {
        _cts = new CancellationTokenSource();
        _cancelled = false;
        _session.StartRecording();
        NotifyStateChanged();

        byte[] audio;
        try
        {
            audio = await audioCapture.RecordAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            _session.Cancel();
            NotifyStateChanged();
            return;
        }
        catch (Exception ex)
        {
            _session.Cancel();
            NotifyStateChanged();
            ErrorOccurred?.Invoke(ex.Message);
            return;
        }

        if (_cancelled)
        {
            _session.Cancel();
            NotifyStateChanged();
            return;
        }

        _session.StopRecording();
        NotifyStateChanged();

        var settings = settingsRepository.Load();
        var profile = settings.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId)
                      ?? settings.Profiles.FirstOrDefault();

        if (profile == null)
        {
            _session.CompleteTranscription(string.Empty);
            ErrorOccurred?.Invoke("No transcription profile configured. Add a profile in Settings.");
            NotifyStateChanged();
            return;
        }

        if (audio.Length == 0)
        {
            _session.CompleteTranscription(string.Empty);
            ErrorOccurred?.Invoke("No audio was captured. Your microphone may be muted or disconnected.");
            NotifyStateChanged();
            return;
        }

        try
        {
            var backend = backendFactory.Create(profile);
            var text = await backend.TranscribeAsync(audio, profile.Language, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(text))
                historyRepository.Add(new TranscriptionHistoryEntry(text, DateTimeOffset.UtcNow));

            await textOutputFactory.Create(settings.TextInsertionMode).InjectTextAsync(text);
            _session.CompleteTranscription(text);
        }
        catch (Exception ex)
        {
            _session.CompleteTranscription(string.Empty);
            ErrorOccurred?.Invoke(ex.Message);
        }
        NotifyStateChanged();
    }

    public void StopRecording() => _cts?.Cancel();

    public void CancelRecording()
    {
        _cancelled = true;
        _cts?.Cancel();
    }
}
