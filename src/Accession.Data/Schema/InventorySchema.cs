using System.Reflection;

namespace Accession.Data.Schema;

public static class InventorySchema
{
    /// <summary>Schema version written by this build of the application.</summary>
    public const int CurrentVersion = 3;

    internal static string LoadScript(string fileName)
    {
        var resourceName = $"Accession.Data.Schema.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema script '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
