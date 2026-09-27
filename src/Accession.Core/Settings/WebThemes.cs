namespace Accession.Core.Settings;

/// <summary>Colour themes of the web UI.</summary>
public static class WebThemes
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>Returns a known theme; anything else becomes <see cref="System"/>.</summary>
    public static string Normalize(string? theme) => theme is Light or Dark ? theme : System;
}
