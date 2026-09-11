using speech2text.Domain;

namespace speech2text.UI.ViewModels;

public class HistoryEntryViewModel(TranscriptionHistoryEntry entry, Action<string> copyToClipboard)
{
    public string Text => entry.Text;
    public DateTimeOffset OccurredAt => entry.OccurredAt;

    public RelayCommand CopyCommand { get; } = new(() => copyToClipboard(entry.Text));
}
