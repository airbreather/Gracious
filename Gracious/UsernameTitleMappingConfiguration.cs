namespace Gracious;

internal sealed record UsernameTitleMappingConfiguration
{
    public required string Username { get; init; }

    public required string StreamTitle { get; init; }
}
