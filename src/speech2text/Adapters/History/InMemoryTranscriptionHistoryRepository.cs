using speech2text.Domain;
using speech2text.Domain.Ports;

namespace speech2text.Adapters.History;

/// <summary>
/// Keeps the last <see cref="_maxEntries"/> transcription results in process memory only —
/// nothing is persisted to disk. Cleared when the application exits.
/// Not thread-safe: entries are only ever added or read from the WPF UI thread
/// (RecordingOrchestrator's async continuations resume there, since the app never uses
/// ConfigureAwait(false)), and RecordingSession's state machine allows only one
/// transcription at a time.
/// </summary>
public class InMemoryTranscriptionHistoryRepository(int maxEntries = 5) : ITranscriptionHistoryRepository
{
    private readonly List<TranscriptionHistoryEntry> _entries = [];

    public void Add(TranscriptionHistoryEntry entry)
    {
        _entries.Insert(0, entry);
        if (_entries.Count > maxEntries)
            _entries.RemoveAt(_entries.Count - 1);
    }

    public IReadOnlyList<TranscriptionHistoryEntry> GetAll() => _entries.AsReadOnly();
}
