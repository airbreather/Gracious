using System.Collections.ObjectModel;

namespace Gracious;

internal sealed record FfmpegArgs
{
    public Collection<string> InputFlags { get; } = new();

    public required string Input { get; init; }

    public Collection<string> OutputFlags { get; } = new();
}
