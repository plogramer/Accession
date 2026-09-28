using System.Globalization;

namespace Accession.Core.Copying;

/// <summary>How copied files are named at the destination (requirements 5.8b, CPY-02).</summary>
public enum CopyNamingMode
{
    /// <summary><c>&lt;destination&gt;\&lt;Media ID&gt;\&lt;folders&gt;\&lt;name&gt;</c>: the original structure and names.</summary>
    PreserveStructure,

    /// <summary><c>&lt;destination&gt;\&lt;prefix&gt;&lt;number&gt;.&lt;ext&gt;</c>, numbered in media, folder, name order.</summary>
    Sequential,

    /// <summary><c>&lt;destination&gt;\&lt;sha1&gt;_&lt;name&gt;</c>: flat, named by content (Copy To). Same SHA-1 and name = one copy.</summary>
    Sha1Name,
}

/// <summary>Naming options. <see cref="Prefix"/>, <see cref="Digits"/> and <see cref="StartNumber"/> apply to sequential names.</summary>
public sealed record CopyNaming
{
    public const int MinDigits = 1;
    public const int MaxDigits = 15;

    public CopyNamingMode Mode { get; init; } = CopyNamingMode.PreserveStructure;
    public string Prefix { get; init; } = string.Empty;
    public int Digits { get; init; } = 8;
    public long StartNumber { get; init; } = 1;

    public static readonly CopyNaming Preserve = new();

    /// <summary>Why these options cannot name <paramref name="fileCount"/> files; null when they can.</summary>
    public string? Validate(long fileCount)
    {
        if (Mode != CopyNamingMode.Sequential)
        {
            return null;
        }

        if (Digits is < MinDigits or > MaxDigits)
        {
            return $"Digits must be between {MinDigits} and {MaxDigits}.";
        }

        if (StartNumber < 0)
        {
            return "The start number cannot be negative.";
        }

        if (Prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Prefix.IndexOfAny(WindowsInvalidNameChars) >= 0)
        {
            return "The prefix contains characters that are not allowed in file names (\\ / : * ? \" < > |).";
        }

        var last = StartNumber + Math.Max(0, fileCount - 1);
        var limit = (long)Math.Pow(10, Digits) - 1;
        if (fileCount > 0 && last > limit)
        {
            return string.Create(CultureInfo.CurrentCulture,
                $"{fileCount:N0} files starting at {StartNumber:N0} need numbers up to {last:N0}, which does not fit in {Digits} digits.");
        }

        return null;
    }

    /// <summary>Sequential file name for number <paramref name="number"/>: prefix + zero-padded number + "." + extension (no dot without one).</summary>
    public string SequentialName(long number, string extension) =>
        Prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0') + (extension.Length == 0 ? string.Empty : "." + extension);

    /// <summary>True when the copies get new names (sequential or SHA-1): a command must be able to rename.</summary>
    public bool Renames => Mode != CopyNamingMode.PreserveStructure;

    /// <summary><c>&lt;sha1&gt;_&lt;name&gt;</c>, the SHA-1 in lowercase; the name keeps its extension.</summary>
    public static string Sha1FileName(string sha1, string name) => sha1.Trim().ToLowerInvariant() + "_" + name;

    /// <summary>An example of the first name, for the dialog.</summary>
    public string Example(string extension = "pdf") => Mode == CopyNamingMode.Sequential ? SequentialName(StartNumber, extension) : string.Empty;

    private static readonly char[] WindowsInvalidNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
}
