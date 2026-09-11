using System.Collections.ObjectModel;
using speech2text.Application;
using speech2text.Domain;
using speech2text.Domain.Ports;

namespace speech2text.UI.ViewModels;

public class OverlayViewModel : ViewModelBase
{
    private readonly RecordingOrchestrator _orchestrator;
    private readonly ISettingsRepository _settingsRepository;

    private RecordingState _state;
    private TranscriptionProfile? _activeProfile;
    private AudioDevice? _selectedDevice;
    private string _errorMessage = string.Empty;
    private bool _isSettingsOpen;
    private bool _isHistoryOpen;

    public ObservableCollection<TranscriptionProfile> Profiles { get; } = [];
    public ObservableCollection<AudioDevice> AudioDevices { get; } = [];

    public RecordingState State
    {
        get => _state;
        private set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(IsRecording));
                OnPropertyChanged(nameof(IsTranscribing));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(RecordingButtonLabel));
                ToggleRecordingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRecording => State == RecordingState.Recording;
    public bool IsTranscribing => State == RecordingState.Transcribing;

    public string StatusText => State switch
    {
        RecordingState.Recording    => "Recording...",
        RecordingState.Transcribing => "Transcribing...",
        _                           => "Ready"
    };

    public string RecordingButtonLabel => State == RecordingState.Recording
        ? "Stop Recording"
        : "Start Recording";

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    /// <summary>
    /// Reflects whether the settings window is currently visible. Set from the View layer
    /// whenever the window's actual visibility changes (button toggle, tray menu, or the
    /// window's own close button), so it stays true to the window regardless of how it changed.
    /// </summary>
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        internal set => SetField(ref _isSettingsOpen, value);
    }

    /// <summary>Reflects whether the history window is currently visible. See <see cref="IsSettingsOpen"/>.</summary>
    public bool IsHistoryOpen
    {
        get => _isHistoryOpen;
        internal set => SetField(ref _isHistoryOpen, value);
    }

    public TranscriptionProfile? ActiveProfile
    {
        get => _activeProfile;
        set
        {
            if (SetField(ref _activeProfile, value) && value != null)
                SaveActiveProfile(value.Id);
        }
    }

    public AudioDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetField(ref _selectedDevice, value))
                SaveSelectedDevice(value);
        }
    }

    public RelayCommand ToggleRecordingCommand { get; }
    public RelayCommand MinimizeCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenHistoryCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand DismissErrorCommand { get; }

    /// <summary>Raised when the user toggles the settings window; carries the requested visibility.</summary>
    public event Action<bool>? SettingsVisibilityRequested;

    /// <summary>Raised when the user toggles the history window; carries the requested visibility.</summary>
    public event Action<bool>? HistoryVisibilityRequested;

    /// <summary>Raised when the user clicks the minimize button — the window should hide to tray.</summary>
    public event Action? MinimizeToTrayRequested;

    /// <summary>Raised when a recording starts while the overlay is hidden — the window should become visible.</summary>
    public event Action? ShowOverlayRequested;

    public OverlayViewModel(
        RecordingOrchestrator orchestrator,
        ISettingsRepository settingsRepository,
        IAudioDeviceEnumerator deviceEnumerator)
    {
        _orchestrator = orchestrator;
        _settingsRepository = settingsRepository;

        _orchestrator.StateChanged += OnOrchestratorStateChanged;
        _orchestrator.ErrorOccurred += OnErrorOccurred;

        ToggleRecordingCommand = new RelayCommand(
            execute:    () =>
            {
                if (_orchestrator.State == RecordingState.Idle)
                    _ = _orchestrator.StartRecordingAsync();
                else if (_orchestrator.State == RecordingState.Recording)
                    _orchestrator.StopRecording();
            },
            canExecute: () => _orchestrator.State != RecordingState.Transcribing);

        MinimizeCommand     = new RelayCommand(() => MinimizeToTrayRequested?.Invoke());
        OpenSettingsCommand = new RelayCommand(() => SettingsVisibilityRequested?.Invoke(!IsSettingsOpen));
        OpenHistoryCommand  = new RelayCommand(() => HistoryVisibilityRequested?.Invoke(!IsHistoryOpen));
        CloseCommand        = new RelayCommand(() => System.Windows.Application.Current.Shutdown());
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = string.Empty);

        LoadFromSettings(deviceEnumerator);
    }

    public void HandleEscapeKey()
    {
        if (_orchestrator.State == RecordingState.Recording)
            _orchestrator.CancelRecording();
        else
            MinimizeToTrayRequested?.Invoke();
    }

    protected virtual void Dispatch(Action action) =>
        System.Windows.Application.Current.Dispatcher.Invoke(action);

    private void OnOrchestratorStateChanged(RecordingState state)
    {
        Dispatch(() =>
        {
            if (state == RecordingState.Recording)
            {
                ErrorMessage = string.Empty;
                ShowOverlayRequested?.Invoke();
            }
            State = state;
        });
    }

    private void OnErrorOccurred(string message)
    {
        Dispatch(() => ErrorMessage = message);
    }

    private void LoadFromSettings(IAudioDeviceEnumerator deviceEnumerator)
    {
        var settings = _settingsRepository.Load();

        foreach (var p in settings.Profiles)
            Profiles.Add(p);

        _activeProfile = Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId)
                         ?? Profiles.FirstOrDefault();

        foreach (var d in deviceEnumerator.GetDevices())
            AudioDevices.Add(d);

        _selectedDevice = AudioDevices.FirstOrDefault(d => d.Id == settings.SelectedAudioDevice?.Id)
                          ?? AudioDevices.FirstOrDefault();
    }

    public void RefreshProfiles(AppSettings settings)
    {
        var previousId = _activeProfile?.Id;
        Profiles.Clear();
        foreach (var p in settings.Profiles)
            Profiles.Add(p);
        _activeProfile = Profiles.FirstOrDefault(p => p.Id == previousId)
                         ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(ActiveProfile));
    }

    private void SaveActiveProfile(Guid profileId)
    {
        var settings = _settingsRepository.Load();
        settings.ActiveProfileId = profileId;
        _settingsRepository.Save(settings);
    }

    private void SaveSelectedDevice(AudioDevice? device)
    {
        var settings = _settingsRepository.Load();
        settings.SelectedAudioDevice = device;
        _settingsRepository.Save(settings);
    }
}
