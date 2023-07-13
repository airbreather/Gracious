/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
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
