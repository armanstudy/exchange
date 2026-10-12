using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace LenjExchange.Desktop.Services;

public interface IDialogService
{
    void Info(string message, string title = "سامانه مالی لنج");
    void Error(string message, string title = "خطا");
    bool Confirm(string message, string title = "تأیید");
    string? Prompt(string message, string title = "ورود اطلاعات", string initial = "");
    string? OpenFile(string filter, string? title = null);
    string? SaveFile(string defaultName, string filter);
    string? PickFolder(string? initial = null);
    void OpenWithShell(string path);
    void PrintWithShell(string path);
}

/// <summary>RTL message boxes and Windows file dialogs.</summary>
public sealed class DialogService : IDialogService
{
    private const MessageBoxOptions Rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

    public void Info(string message, string title = "سامانه مالی لنج") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, Rtl);

    public void Error(string message, string title = "خطا") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, Rtl);

    public bool Confirm(string message, string title = "تأیید") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, Rtl) == MessageBoxResult.Yes;

    public string? Prompt(string message, string title = "ورود اطلاعات", string initial = "")
    {
        var dlg = new Views.PromptWindow(message, title, initial) { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    public string? OpenFile(string filter, string? title = null)
    {
        var d = new OpenFileDialog { Filter = filter, Title = title ?? "انتخاب فایل" };
        return d.ShowDialog() == true ? d.FileName : null;
    }

    public string? SaveFile(string defaultName, string filter)
    {
        var d = new SaveFileDialog { FileName = defaultName, Filter = filter, OverwritePrompt = true };
        return d.ShowDialog() == true ? d.FileName : null;
    }

    public string? PickFolder(string? initial = null)
    {
        var d = new OpenFolderDialog { InitialDirectory = initial ?? "" };
        return d.ShowDialog() == true ? d.FolderName : null;
    }

    public void OpenWithShell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>Sends the PDF to the default printer through the registered PDF viewer's "print" verb.</summary>
    public void PrintWithShell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });
}
