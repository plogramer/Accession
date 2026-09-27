using Accession.Core.Model;

namespace Accession.Core.Scanning;

/// <summary>A classified scan error.</summary>
/// <param name="IsNetwork">The share or network is unavailable; the operation should be retried (SCN-07).</param>
public sealed record ScanErrorInfo(ScanErrorType Type, ScanErrorSeverity Severity, int? ErrorCode, string Message, bool IsNetwork);

/// <summary>Maps exceptions and Win32 error codes to <see cref="ScanErrorType"/> (requirement SCN-51).</summary>
public static class ScanErrorClassifier
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorInvalidName = 123;
    private const int ErrorFilenameExcedRange = 206;

    // Network / share unavailable.
    private static readonly HashSet<int> NetworkCodes =
    [
        51,   // ERROR_REM_NOT_LIST
        53,   // ERROR_BAD_NETPATH
        54,   // ERROR_NETWORK_BUSY
        59,   // ERROR_UNEXP_NET_ERR
        64,   // ERROR_NETNAME_DELETED
        67,   // ERROR_BAD_NET_NAME
        121,  // ERROR_SEM_TIMEOUT
        1231, // ERROR_NETWORK_UNREACHABLE
        1232, // ERROR_HOST_UNREACHABLE
        1236, // ERROR_CONNECTION_ABORTED
        1311, // ERROR_NO_LOGON_SERVERS
        2250, // ERROR_NOT_CONNECTED
    ];

    public static bool IsNetworkCode(int code) => NetworkCodes.Contains(code);

    public static ScanErrorInfo Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var code = Win32Code(exception.HResult);
        var message = exception.Message;

        if (code is { } c && NetworkCodes.Contains(c))
        {
            return new ScanErrorInfo(ScanErrorType.IOError, ScanErrorSeverity.Error, c, message, IsNetwork: true);
        }

        return exception switch
        {
            UnauthorizedAccessException => Error(ScanErrorType.AccessDenied, code ?? ErrorAccessDenied, message),
            FileNotFoundException or DirectoryNotFoundException => Error(ScanErrorType.NotFound, code, message),
            PathTooLongException => Error(ScanErrorType.PathError, code, message),
            _ => code switch
            {
                ErrorAccessDenied => Error(ScanErrorType.AccessDenied, code, message),
                ErrorSharingViolation or ErrorLockViolation => Error(ScanErrorType.FileLocked, code, message),
                ErrorFileNotFound or ErrorPathNotFound => Error(ScanErrorType.NotFound, code, message),
                ErrorInvalidName or ErrorFilenameExcedRange => Error(ScanErrorType.PathError, code, message),
                _ when exception is IOException => Error(ScanErrorType.IOError, code, message),
                _ => Error(ScanErrorType.Other, code, message),
            },
        };
    }

    /// <summary>Win32 error code from an HRESULT with FACILITY_WIN32 (0x8007xxxx), otherwise the HRESULT itself.</summary>
    public static int? Win32Code(int hresult)
    {
        if (hresult == 0)
        {
            return null;
        }

        return (hresult & 0xFFFF0000) == 0x80070000 ? hresult & 0xFFFF : hresult;
    }

    private static ScanErrorInfo Error(ScanErrorType type, int? code, string message) =>
        new(type, ScanErrorSeverity.Error, code, message, IsNetwork: false);
}
