using System.Collections.ObjectModel;

namespace Gracious;

internal sealed record GraciousConfiguration
{
    public required string DiscordBotToken { get; init; }

    public required string EmergencyFolder { get; init; }

    public required string MusicFolder { get; init; }

    public required string OutputFile { get; init; }

    public required string DownloadUrl { get; init; }

    public required bool RegisterDefaultApplicationCommands { get; init; }

    public Collection<UsernameTitleMappingConfiguration> UsernameTitleMappings { get; } = new();

    public Collection<ulong> GuildIdsForApplicationCommands { get; } = new();

    public required DesktopRecordingFfmpegArgs DesktopRecordingFfmpegArgs { get; init; }
}
