using System.Reflection;
using Accession.App.Mvvm;

namespace Accession.App.ViewModels;

/// <summary>Placeholder landing screen until the Start window (inventory list) is built.</summary>
public sealed class HomeViewModel : ViewModelBase
{
    public string AppName => "Accession";

    public string Tagline => "Inventory, hash, and report every media you receive.";

    public string Version { get; } =
        typeof(HomeViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? string.Empty;
}
