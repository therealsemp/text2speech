using System.Windows;
using speech2text.UI.ViewModels;

namespace speech2text.UI.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        (DataContext as HistoryViewModel)?.Refresh();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
}
