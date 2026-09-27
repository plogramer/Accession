using System.Diagnostics;
using System.IO;
using System.Windows;
using Accession.App.Mvvm;
using Accession.Core.Runtime;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

public sealed partial class ErrorDialogViewModel : DialogViewModelBase
{
    public ErrorDialogViewModel(string title, string message, Exception? exception)
    {
        Title = title;
        Message = message;
        Details = exception?.ToString() ?? string.Empty;
    }

    public override bool CanResize => true;

    public string Message { get; }

    public string Details { get; }

    public bool HasDetails => Details.Length > 0;

    public string LogFolder => AppPaths.LogFolder;

    [RelayCommand]
    private void CopyDetails() => Clipboard.SetText($"{Message}{Environment.NewLine}{Environment.NewLine}{Details}");

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LogFolder);
        Process.Start(new ProcessStartInfo { FileName = LogFolder, UseShellExecute = true });
    }

    [RelayCommand]
    private void Ok() => Close(true);
}
