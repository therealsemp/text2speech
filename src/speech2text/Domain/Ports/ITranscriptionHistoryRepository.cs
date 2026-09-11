namespace speech2text.Domain.Ports;

/// <summary>
/// Keeps track of recent transcription results so the user can recover one even if
/// text injection at the cursor position failed (e.g. focus was lost before pasting).
/// How many entries are retained is up to the implementation.
/// </summary>
public interface ITranscriptionHistoryRepository
{
    void Add(TranscriptionHistoryEntry entry);

    /// <summary>Retained entries, most recent first.</summary>
    IReadOnlyList<TranscriptionHistoryEntry> GetAll();
}
