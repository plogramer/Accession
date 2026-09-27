using System.Reflection;
using Accession.Core;
using Accession.Data;

namespace Accession.Tests;

public class ArchitectureTests
{
    private static readonly string[] UiAssemblies =
    [
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Xaml",
        "System.Windows.Forms",
    ];

    [Fact]
    public void Core_does_not_reference_ui_assemblies()
    {
        AssertNoUiReferences(typeof(CoreAssembly).Assembly);
    }

    [Fact]
    public void Data_does_not_reference_ui_assemblies()
    {
        AssertNoUiReferences(typeof(DataAssembly).Assembly);
    }

    private static void AssertNoUiReferences(Assembly assembly)
    {
        var references = assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.DoesNotContain(references, name => UiAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase));
    }
}
