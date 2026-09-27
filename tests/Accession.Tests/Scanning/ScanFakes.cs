using System.Collections.Concurrent;
using Accession.Core.Scanning;

namespace Accession.Tests.Scanning;

/// <summary>Real lister with injectable failures per path.</summary>
public sealed class FakeLister : IDirectoryLister
{
    private readonly FileSystemDirectoryLister _real = new();

    /// <summary>Returns an exception to throw for a full path, or null to list normally.</summary>
    public Func<string, Exception?> FailWith { get; set; } = _ => null;

    public IReadOnlyList<DirectoryEntry> List(string fullPath) =>
        FailWith(fullPath) is { } ex ? throw ex : _real.List(fullPath);

    public DirectoryEntry GetDirectory(string fullPath) => _real.GetDirectory(fullPath);
}

/// <summary>Real SHA-1 hasher with hooks: block, fail or alter results per path, and count calls.</summary>
public sealed class FakeHasher : IFileHasher
{
    private readonly Sha1FileHasher _real = new();

    public ConcurrentBag<string> Calls { get; } = [];

    /// <summary>Called before hashing; may block (e.g. on a gate) to hold a file in progress.</summary>
    public Action<string, CancellationToken> Before { get; set; } = (_, _) => { };

    public Func<string, Exception?> FailWith { get; set; } = _ => null;

    public Func<string, FileHashResult, FileHashResult> Alter { get; set; } = (_, r) => r;

    public FileHashResult Hash(string fullPath, CancellationToken cancellationToken)
    {
        Calls.Add(fullPath);
        Before(fullPath, cancellationToken);
        if (FailWith(fullPath) is { } ex)
        {
            throw ex;
        }

        return Alter(fullPath, _real.Hash(fullPath, cancellationToken));
    }
}

public static class Win32Errors
{
    public static IOException Create(int code, string message = "simulated") => new(message, unchecked((int)0x80070000) | code);
}
