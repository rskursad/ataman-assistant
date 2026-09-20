using System.Collections.Specialized;
using AtamanAssistant.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AtamanAssistant.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Transcript.CollectionChanged -= OnTranscriptChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.Transcript.CollectionChanged += OnTranscriptChanged;
        }
    }

    private void OnTranscriptChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ChatScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
    }
}