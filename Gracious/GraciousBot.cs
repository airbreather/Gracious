/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
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
