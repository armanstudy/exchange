using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LenjExchange.Desktop.ViewModels;

namespace LenjExchange.Desktop.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnVmPropertyChanged;
        ApplyHeaders();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportsViewModel.Headers)) ApplyHeaders();
    }

    /// <summary>DataGrid columns are not in the visual tree, so per-report headers are applied here.</summary>
    private void ApplyHeaders()
    {
        if (DataContext is not ReportsViewModel vm) return;
        for (var i = 0; i < ReportGrid.Columns.Count; i++)
        {
            var header = i < vm.Headers.Count ? vm.Headers[i] : "";
            ReportGrid.Columns[i].Header = header;
            ReportGrid.Columns[i].Visibility = string.IsNullOrEmpty(header) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
