namespace Gracious;

internal sealed record DesktopRecordingFfmpegArgs
{
    public required FfmpegArgs DesktopScreen { get; init; }

    public required FfmpegArgs DesktopAudio { get; init; }
}
