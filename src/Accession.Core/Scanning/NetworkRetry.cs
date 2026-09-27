namespace Accession.Core.Scanning;

/// <summary>
/// Retries file system operations that fail because the share or network is unavailable (SCN-07): waits
/// 5 s, 15 s and 45 s between attempts, then calls <c>onUnavailable</c> (the coordinator pauses the scan and
/// waits for the user to resume) and starts over. Other errors are rethrown immediately.
/// </summary>
public sealed class NetworkRetry
{
    public static readonly IReadOnlyList<TimeSpan> DefaultDelays =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];

    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<TimeSpan> _delays;
    private readonly Func<Exception, CancellationToken, Task> _onUnavailable;

    public NetworkRetry(TimeProvider timeProvider, Func<Exception, CancellationToken, Task> onUnavailable, IReadOnlyList<TimeSpan>? delays = null)
    {
        _timeProvider = timeProvider;
        _onUnavailable = onUnavailable;
        _delays = delays ?? DefaultDelays;
    }

    public async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return operation();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ScanErrorClassifier.Classify(ex).IsNetwork)
            {
                if (attempt < _delays.Count)
                {
                    await Task.Delay(_delays[attempt++], _timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _onUnavailable(ex, cancellationToken).ConfigureAwait(false);
                attempt = 0;
            }
        }
    }
}
