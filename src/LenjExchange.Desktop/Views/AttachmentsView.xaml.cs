using System.Windows.Controls;
using System.Windows.Input;
using LenjExchange.Desktop.ViewModels;

namespace LenjExchange.Desktop.Views;

public partial class AttachmentsView : UserControl
{
    public AttachmentsView() => InitializeComponent();

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AttachmentsViewModel vm && vm.OpenCommand.CanExecute(null)) vm.OpenCommand.Execute(null);
    }
}
