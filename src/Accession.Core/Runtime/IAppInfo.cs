using System.Reflection;

namespace Accession.Core.Runtime;

/// <summary>Application version, recorded in inventories, scan logs and migrations.</summary>
public interface IAppInfo
{
    /// <summary>Informational version without build metadata, e.g. <c>0.1.0</c>.</summary>
    string Version { get; }
}

public sealed class AssemblyAppInfo(Assembly assembly) : IAppInfo
{
    public string Version { get; } =
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
