using System.Text;

namespace Accession.Core.Copying;

/// <summary>
/// The command written to the copy batch for each file (CPY-05). Placeholders: <c>{source}</c> (full path of the file),
/// <c>{destination}</c> (full path of the copy), <c>{sourcedir}</c>, <c>{sourcename}</c>, <c>{destdir}</c>, <c>{destname}</c>.
/// A placeholder that is not already inside quotes is quoted, so paths with spaces work.
/// </summary>
public sealed record CopyCommandTemplate(string Name, string Command)
{
    public static readonly CopyCommandTemplate Copy = new("copy", "copy /Y {source} {destination} >nul");

    /// <summary>robocopy copies a file by folder and name, so it cannot rename: preserved structure only.</summary>
    public static readonly CopyCommandTemplate Robocopy = new("robocopy",
        "robocopy {sourcedir} {destdir} {sourcename} /COPY:DAT /DCOPY:T /R:1 /W:1 /NFL /NDL /NJH /NJS /NP >nul");

    public static IReadOnlyList<CopyCommandTemplate> Presets { get; } = [Copy, Robocopy];

    public static readonly IReadOnlyList<string> Placeholders = ["{source}", "{destination}", "{sourcedir}", "{sourcename}", "{destdir}", "{destname}"];

    /// <summary>True for a robocopy command. Its exit codes 0–7 mean success, 8 and above failure.</summary>
    public bool IsRobocopy => FirstWord(Command).Equals("robocopy", StringComparison.OrdinalIgnoreCase)
                              || FirstWord(Command).Equals("robocopy.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lowest exit code that means the command failed.</summary>
    public int FailureExitCode => IsRobocopy ? 8 : 1;

    /// <summary>Why this command cannot be used with <paramref name="naming"/>; null when it can.</summary>
    public string? Validate(CopyNamingMode naming)
    {
        if (string.IsNullOrWhiteSpace(Command))
        {
            return "Enter a copy command.";
        }

        if (Command.Contains('\n', StringComparison.Ordinal) || Command.Contains('\r', StringComparison.Ordinal))
        {
            return "The copy command must be on one line.";
        }

        var hasSource = Has("{source}") || (Has("{sourcedir}") && Has("{sourcename}"));
        if (!hasSource)
        {
            return "The command needs {source}, or {sourcedir} and {sourcename}.";
        }

        if (!Has("{destination}") && !Has("{destdir}"))
        {
            return "The command needs {destination} or {destdir}.";
        }

        if (naming == CopyNamingMode.Sequential)
        {
            if (IsRobocopy)
            {
                return "robocopy cannot rename files, so it only works with \"Keep the original folders and names\". Use copy for sequential names.";
            }

            if (!Has("{destination}") && !Has("{destname}"))
            {
                return "Sequential names need {destination} or {destname} in the command; otherwise the files keep their original names.";
            }
        }

        return null;
    }

    /// <summary>The command for one file, ready for a .bat file (% doubled, placeholders quoted).</summary>
    public string Render(string source, string destination)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{source}"] = source,
            ["{destination}"] = destination,
            ["{sourcedir}"] = DirectoryOf(source),
            ["{sourcename}"] = Path.GetFileName(source),
            ["{destdir}"] = DirectoryOf(destination),
            ["{destname}"] = Path.GetFileName(destination),
        };

        var text = new StringBuilder(Command.Length + 2 * (source.Length + destination.Length));
        var i = 0;
        while (i < Command.Length)
        {
            var placeholder = Command[i] == '{' ? Placeholders.FirstOrDefault(p => string.Compare(Command, i, p, 0, p.Length, StringComparison.OrdinalIgnoreCase) == 0) : null;
            if (placeholder is null)
            {
                text.Append(Command[i] == '%' ? "%%" : Command[i].ToString());
                i++;
                continue;
            }

            var quoted = i > 0 && Command[i - 1] == '"';
            var value = BatchFile.EscapePercent(values[placeholder]);
            text.Append(quoted ? value : "\"" + value + "\"");
            i += placeholder.Length;
        }

        return text.ToString();
    }

    private bool Has(string placeholder) => Command.Contains(placeholder, StringComparison.OrdinalIgnoreCase);

    /// <summary>Folder of a path without a trailing separator ("C:\a\b" → "C:\a"): a trailing \ before a quote breaks robocopy.</summary>
    private static string DirectoryOf(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        return folder.EndsWith('\\') || folder.EndsWith('/') ? folder + "." : folder; // "D:\" → "D:\."
    }

    private static string FirstWord(string command)
    {
        var trimmed = command.TrimStart().TrimStart('@').Trim('"');
        var end = trimmed.IndexOfAny([' ', '\t', '"']);
        var word = end < 0 ? trimmed : trimmed[..end];
        return Path.GetFileName(word);
    }
}

/// <summary>Helpers for writing .bat files.</summary>
public static class BatchFile
{
    /// <summary>% starts a variable in a batch file; %% is a literal %.</summary>
    public static string EscapePercent(string text) => text.Replace("%", "%%", StringComparison.Ordinal);

    /// <summary>A path inside double quotes for <c>if exist</c>, <c>mkdir</c> and <c>echo</c> lines.</summary>
    public static string Quote(string path) => "\"" + EscapePercent(path) + "\"";
}
