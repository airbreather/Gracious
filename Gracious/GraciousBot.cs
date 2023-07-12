using DSharpPlus;

using Microsoft.Extensions.Hosting;

namespace Gracious;

internal sealed class GraciousBot : IHostedService
{
    private readonly DiscordClient _discord;

    public GraciousBot(DiscordClient discord)
    {
        _discord = discord;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _discord.ConnectAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _discord.DisconnectAsync();
    }
}
