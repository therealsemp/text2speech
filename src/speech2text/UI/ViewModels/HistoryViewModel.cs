using System.Collections.ObjectModel;
using speech2text.Domain.Ports;
using Clipboard = System.Windows.Clipboard;

namespace speech2text.UI.ViewModels;

public class HistoryViewModel(ITranscriptionHistoryRepository historyRepository) : ViewModelBase
{
    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Reloads entries from the repository — call each time the window is shown.</summary>
    public void Refresh()
    {
        Entries.Clear();
        foreach (var entry in historyRepository.GetAll())
            Entries.Add(new HistoryEntryViewModel(entry, Clipboard.SetText));

        OnPropertyChanged(nameof(HasEntries));
    }
}
