using System.IO;
using Accession.Core.Runtime;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

public sealed partial class ErrorDialogViewModel : DialogViewModelBase
{
    private readonly IDesktop _desktop;

    public ErrorDialogViewModel(string title, string message, Exception? exception, IDesktop desktop)
    {
        _desktop = desktop;
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
    private void CopyDetails() => _desktop.SetClipboardText($"{Message}{Environment.NewLine}{Environment.NewLine}{Details}");

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LogFolder);
        _desktop.OpenFolder(LogFolder);
    }

    [RelayCommand]
    private void Ok() => Close(true);
}
