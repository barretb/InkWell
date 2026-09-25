namespace InkWell.Infrastructure.Tests.Export;

public class EpubCheckValidatorTests
{
    [Theory]
    [InlineData(0, "No errors detected", true)]
    [InlineData(1, "WARNING: invalid content", false)]
    [InlineData(2, "", false)]
    [InlineData(0, "ERROR: invalid content", false)]
    [InlineData(0, "FATAL: failed", false)]
    public void Validation_requires_a_successful_process_exit(int exitCode, string report, bool expected)
    {
        EpubCheckResult result = EpubCheckValidator.ResultFromExit(exitCode, report);
        Assert.Equal(expected, result.IsValid);
        Assert.Contains($"Exit code: {exitCode}", result.Report);
    }
}
