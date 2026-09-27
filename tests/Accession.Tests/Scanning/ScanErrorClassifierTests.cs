using Accession.Core.Model;
using Accession.Core.Scanning;

namespace Accession.Tests.Scanning;

public class ScanErrorClassifierTests
{
    private static IOException Win32(int code) => new($"win32 {code}", unchecked((int)0x80070000) | code);

    [Theory]
    [InlineData(5, ScanErrorType.AccessDenied)]
    [InlineData(32, ScanErrorType.FileLocked)]
    [InlineData(33, ScanErrorType.FileLocked)]
    [InlineData(2, ScanErrorType.NotFound)]
    [InlineData(3, ScanErrorType.NotFound)]
    [InlineData(123, ScanErrorType.PathError)]
    [InlineData(206, ScanErrorType.PathError)]
    [InlineData(1117, ScanErrorType.IOError)]
    public void Win32_codes_map_to_error_types(int code, ScanErrorType expected)
    {
        var info = ScanErrorClassifier.Classify(Win32(code));

        Assert.Equal(expected, info.Type);
        Assert.Equal(code, info.ErrorCode);
        Assert.False(info.IsNetwork);
        Assert.Equal(ScanErrorSeverity.Error, info.Severity);
    }

    [Theory]
    [InlineData(53)]
    [InlineData(64)]
    [InlineData(67)]
    [InlineData(1231)]
    [InlineData(59)]
    public void Network_codes_are_flagged_for_retry(int code)
    {
        var info = ScanErrorClassifier.Classify(Win32(code));

        Assert.True(info.IsNetwork);
        Assert.Equal(ScanErrorType.IOError, info.Type);
    }

    [Fact]
    public void Exception_types_map_without_codes()
    {
        Assert.Equal(ScanErrorType.AccessDenied, ScanErrorClassifier.Classify(new UnauthorizedAccessException()).Type);
        Assert.Equal(ScanErrorType.NotFound, ScanErrorClassifier.Classify(new DirectoryNotFoundException()).Type);
        Assert.Equal(ScanErrorType.NotFound, ScanErrorClassifier.Classify(new FileNotFoundException()).Type);
        Assert.Equal(ScanErrorType.PathError, ScanErrorClassifier.Classify(new PathTooLongException()).Type);
        Assert.Equal(ScanErrorType.Other, ScanErrorClassifier.Classify(new InvalidOperationException("x")).Type);
    }

    [Fact]
    public void Win32_code_extraction()
    {
        Assert.Equal(5, ScanErrorClassifier.Win32Code(unchecked((int)0x80070005)));
        Assert.Equal(unchecked((int)0x80004005), ScanErrorClassifier.Win32Code(unchecked((int)0x80004005)));
        Assert.Null(ScanErrorClassifier.Win32Code(0));
    }
}
