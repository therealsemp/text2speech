namespace speech2text.Domain;

/// <summary>
/// A past transcription result, kept so the user can recover it (e.g. via copy)
/// if text injection at the cursor position failed or was not what they expected.
/// </summary>
public record TranscriptionHistoryEntry(string Text, DateTimeOffset OccurredAt);
