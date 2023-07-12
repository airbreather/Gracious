namespace Gracious;

internal sealed record UsernameTitleMappingConfiguration
{
    public string Username { get; init; } = string.Empty;

    public string StreamTitle { get; init; } = string.Empty;
}
