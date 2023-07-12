namespace Gracious;

internal sealed class ProcessExitedWithCodeException : Exception
{
    public ProcessExitedWithCodeException(int? exitCode)
        : base($"Process exited with code {exitCode?.ToString() ?? "(unknown)"}")
    {
        ExitCode = exitCode;
    }

    public int? ExitCode { get; }
}
