using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Services;
using CSIViewer.Host.Services;

namespace CSIViewer.Host.Views;

public sealed partial class HistoryPage : Page
{
    private readonly AppState _state;
    private readonly IHistoryRepository _repository;
    private readonly ObservableCollection<ConnectionHistoryEntry> _filtered = [];
    private int _filterScheduled;

    public HistoryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _repository = App.Services.GetRequiredService<IHistoryRepository>();
        HistoryList.ItemsSource = _filtered;
        _state.PropertyChanged += (_, _) => ScheduleFilter();
        _state.History.CollectionChanged += (_, _) => ScheduleFilter();
        Loaded += (_, _) => ApplyFilter();
    }

    private void ScheduleFilter()
    {
        if (Interlocked.Exchange(ref _filterScheduled, 1) != 0)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _filterScheduled, 0);
            ApplyFilter();
        });
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ResultFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_state.History.Count == 0)
        {
            HistoryInfoBar.Severity = InfoBarSeverity.Informational;
            HistoryInfoBar.Title = global::CSIViewer.Core.Localization.Text("No hay historial");
            HistoryInfoBar.Message = global::CSIViewer.Core.Localization.Text("Todavía no hay conexiones guardadas que borrar.");
            HistoryInfoBar.IsOpen = true;
            return;
        }

        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = XamlRoot,
            Title = global::CSIViewer.Core.Localization.Text("Borrar todo el historial"),
            Content = global::CSIViewer.Core.Localization.Text("Se eliminarán permanentemente todas las conexiones guardadas en este equipo. ") +
                      global::CSIViewer.Core.Localization.Text("No se borran los dispositivos, los contactos ni las sesiones activas."),
            PrimaryButtonText = "Borrar todo",
            CloseButtonText = global::CSIViewer.Core.Localization.Text("Cancelar"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _repository.DeleteAllAsync();
            _state.History.Clear();
            HistoryInfoBar.Severity = InfoBarSeverity.Success;
            HistoryInfoBar.Title = "Historial borrado";
            HistoryInfoBar.Message = global::CSIViewer.Core.Localization.Text("Todas las conexiones guardadas se han eliminado.");
            HistoryInfoBar.IsOpen = true;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            HistoryInfoBar.Severity = InfoBarSeverity.Error;
            HistoryInfoBar.Title = global::CSIViewer.Core.Localization.Text("No se pudo borrar el historial");
            HistoryInfoBar.Message = ex.GetBaseException().Message;
            HistoryInfoBar.IsOpen = true;
        }
    }

    private void ApplyFilter()
    {
        if (_state is null || SearchBox is null || ResultFilter is null)
        {
            return;
        }

        var query = SearchBox.Text?.Trim() ?? "";
        var resultFilter = ResultFilter.SelectedIndex;
        _filtered.Clear();
        foreach (var entry in _state.History.Where(x =>
                     (query.Length == 0 ||
                      x.RemoteName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                      x.RemoteDeviceId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                      x.DisconnectReason.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                     (resultFilter == 0 ||
                      (resultFilter == 1 && x.Result == ConnectionResult.Success) ||
                      (resultFilter == 2 && x.Result != ConnectionResult.Success))))
        {
            _filtered.Add(entry);
        }
    }
}
