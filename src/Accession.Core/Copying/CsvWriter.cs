using System.Text;

namespace Accession.Core.Copying;

/// <summary>
/// Streams a CSV file (UTF-8 with BOM, so Excel shows accented names correctly; every field quoted; CRLF). Written to
/// <c>path.partial</c> and moved into place by <see cref="Complete"/>.
/// </summary>
public sealed class CsvWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly string _path;
    private readonly string _partialPath;
    private bool _completed;

    public CsvWriter(string path, IReadOnlyList<string> headers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _partialPath = path + ".partial";
        if (System.IO.Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        _writer = new StreamWriter(_partialPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1 << 16) { NewLine = "\r\n" };
        WriteRow(headers);
    }

    public string Path => _path;

    public long Rows { get; private set; }

    public void WriteRow(IReadOnlyList<string?> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                _writer.Write(',');
            }

            _writer.Write('"');
            _writer.Write((fields[i] ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal));
            _writer.Write('"');
        }

        _writer.WriteLine();
        Rows++;
    }

    /// <summary>Closes the file and moves it to its final name (replacing an older file of that name).</summary>
    public void Complete()
    {
        _writer.Dispose();
        File.Move(_partialPath, _path, overwrite: true);
        _completed = true;
    }

    /// <summary>Without <see cref="Complete"/>, the partial file is deleted.</summary>
    public void Dispose()
    {
        _writer.Dispose();
        if (!_completed)
        {
            try
            {
                File.Delete(_partialPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do; the .partial name says it is incomplete.
            }
        }
    }
}
