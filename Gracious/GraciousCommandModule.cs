/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using DSharpPlus.VoiceNext;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;

using ZstdNet;

namespace Gracious;

internal sealed class GraciousCommandModule : ApplicationCommandModule
{
    private const string SourceOffer = "*Gracious is free (AGPL-3.0-or-later) software.  Its source code is available at: `https://github.com/airbreather/Gracious`*";

    private readonly GraciousSessions _sessions;

    private readonly IOptionsMonitor<GraciousConfiguration> _cfg;

    private readonly ILogger<GraciousCommandModule> _logger;

    public GraciousCommandModule(GraciousSessions sessions, IOptionsMonitor<GraciousConfiguration> cfg, ILogger<GraciousCommandModule> logger)
    {
        _sessions = sessions;
        _cfg = cfg;
        _logger = logger;
    }

    [SlashRequireGuild]
    [SlashCommand("join", "Orders Gracious to join the listed voice channel.")]
    public async Task JoinCommand(
        InteractionContext ctx,
        [Option("channel", "What channel to join, or leave it blank for me to join your current one."), ChannelTypes(ChannelType.Voice)] DiscordChannel? channel = null)
    {
        try
        {
            await Core();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Demystify(), "An unexpected error occurred during the '/join' command.");
        }

        async ValueTask Core()
        {
            GraciousConfiguration cfg = _cfg.CurrentValue;

            if (ctx.Member is not DiscordMember member)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("This command needs to be run from a Discord server."));
                return;
            }

            if ((channel ??= member.VoiceState?.Channel) is null)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                    .AddEmbed(new DiscordEmbedBuilder().WithImageUrl("https://c.tenor.com/HtRhRx4-yS4AAAAC/im-not-sure-where-im-supposed-to-go-butters-stotch.gif")));
                return;
            }

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"Joining voice channel '{channel!.Name}'...\n\n{SourceOffer}"));
            try
            {
                GraciousSession session = await _sessions.BeginAsync(ctx.User, channel);
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Joined voice channel '{channel!.Name}'.  You will need this ID to retrieve your recording, remember it: `{session.SessionId}`"));

                if ((await ReadMusicIndexAsync(cfg)).TryGetValue("now-recording", out string? origPath))
                {
                    string pcmPath = origPath + ".pcm.zst";
                    if (!File.Exists(pcmPath))
                    {
                        await PrepareAsync(origPath, pcmPath);
                    }

                    session.PlayMusic(pcmPath, () => Task.CompletedTask);
                }
            }
            catch (SessionAlreadyStartedException)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Hold up, I'm already recording something on '{ctx.Guild.Name}'... or at least I think I'm supposed to be.  Is this some kind of a joke?"));
            }
        }
    }

    [SlashRequireGuild]
    [SlashCommand("leave", "Orders Gracious to leave its current channel and finish recording.")]
    public async Task LeaveCommand(InteractionContext ctx)
    {
        try
        {
            await Core();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Demystify(), "An unexpected error occurred during the '/leave' command.");
        }

        async ValueTask Core()
        {
            if (ctx.Guild is null)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("This command needs to be run from a Discord server."));
                return;
            }

            if (ctx.Client.GetVoiceNext().GetConnection(ctx.Guild) is VoiceNextConnection conn)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"Stopping the recording that's in progress for '{conn.TargetChannel.Name}'..."));

                try
                {
                    GraciousSession session = await _sessions.EndAsync(ctx.User, ctx.Guild.Id);
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Finished recording and left '{conn.TargetChannel.Name}'.  Remember your recording ID!  Here it is again: `{session.SessionId}`"));
                }
                catch (SessionStartedBySomeoneElseException)
                {
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                        .AddEmbed(new DiscordEmbedBuilder().WithImageUrl("https://c.tenor.com/CKfw-90gBOIAAAAC/judge-judy-no.gif"))
                        .WithContent($"HEY, WAIT A MINUTE.  You weren't the one who started this recording session!  BAD {ctx.User.Username}#{ctx.User.Discriminator}!"));
                }
                catch (SessionDoesNotExistException)
                {
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                        .AddEmbed(new DiscordEmbedBuilder().WithImageUrl("https://c.tenor.com/WnjJsVOwoJQAAAAC/john-travolta-well.gif"))
                        .AddEmbed(new DiscordEmbedBuilder().WithImageUrl("https://c.tenor.com/BiUtqfsTcqcAAAAC/memory-no-memory.gif"))
                        .AddEmbed(new DiscordEmbedBuilder().WithImageUrl("https://c.tenor.com/CUKos6wabgAAAAAC/the-simpsons-homer.gif")));
                    conn.Disconnect();
                }
            }
            else
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder()
                        .AddEmbed(new DiscordEmbedBuilder().WithImageUrl("https://c.tenor.com/ihqN6a3iiYEAAAAd/pikachu-shocked-face-stunned.gif"))
                        .WithContent($"I'm not in a voice channel on '{ctx.Guild.Name}', so your '/leave' command just did nothing."));
            }
        }
    }

    [SlashCommand("finalize", "Makes Gracious bundle up all the raw captured data into a file that anyone can download.")]
    public async Task FinalizeCommand(
        InteractionContext ctx,
        [Option("session-id", "That magical session ID that you were given during /join and /leave.")] long sessionId)
    {
        try
        {
            await Core();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Demystify(), "An unexpected error occurred during the '/finalize' command.");
        }

        async ValueTask Core()
        {
            GraciousConfiguration cfg = _cfg.CurrentValue;

            JankStringTemplateParameters jank = new((uint)sessionId);

            string emergencyFolderPath = jank.Resolve(cfg.EmergencyFolder);
            string rawFilePath = Path.Combine(emergencyFolderPath, "raw.dat");
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"Started building the output file for you to download.  It takes a while, please be patient..."));

            try
            {
                await GraciousFinalize.FinalizeAsync(ctx.Client.CurrentUser, rawFilePath, Path.Combine(emergencyFolderPath, Path.GetRandomFileName()), jank.Resolve(cfg.OutputFile));

                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Finished!  Download your file now: {jank.Resolve(cfg.DownloadUrl)}"));
            }
            catch (SessionDoesNotExistException)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Those four words look valid, but something went wrong when trying to actually look up that session (it probably got deleted at some point, to save space)."));
            }
        }
    }

    [SlashRequireOwner]
    [SlashCommand("prepare", "Orders Gracious to prepare all music tracks for streaming.")]
    public async Task PrepareCommand(InteractionContext ctx)
    {
        try
        {
            await Core();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Demystify(), "An unexpected error occurred during the '/prepare' command.");
        }

        async ValueTask Core()
        {
            GraciousConfiguration cfg = _cfg.CurrentValue;
            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"Preparing all music files for streaming (compression level: {CompressionOptions.MaxCompressionLevel})..."));
            await Parallel.ForEachAsync((await ReadMusicIndexAsync(cfg)).Values, async (origPath, cancellationToken) =>
            {
                string pcmPath = origPath + ".pcm.zst";
                if (!File.Exists(pcmPath))
                {
                    await PrepareAsync(origPath, pcmPath);
                }
            });

            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Done.  All streamable music files are ready for immediate streaming."));
        }
    }

    [SlashRequireGuild]
    [SlashCommand("play", "Orders Gracious to start playing a music track that I have on my PC.")]
    public async Task PlayCommand(
        InteractionContext ctx,
        [Option("file", "The name of the music file that I can stream.")] string file)
    {
        try
        {
            await Core();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Demystify(), "An unexpected error occurred during the '/play' command.");
        }

        async ValueTask Core()
        {
            if (ctx.Guild is null)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("This command needs to be run from a Discord server."));
                return;
            }

            if (await _sessions.ForGuildIdAsync(ctx.Guild!.Id) is not GraciousSession session)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("I'm not currently recording anything.  I can only play if I'm also recording.  It's... kinda my thing."));
                return;
            }

            GraciousConfiguration cfg = _cfg.CurrentValue;
            if (!(await ReadMusicIndexAsync(cfg)).TryGetValue(file, out string? origPath))
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"'{file}' is not a music file that I can stream."));
                return;
            }

            string pcmPath = origPath + ".pcm.zst";
            if (File.Exists(pcmPath))
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"Playing '{file}' now..."));
            }
            else
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"'{file}' was added since the last '/prepare' command, so it's not immediately ready for streaming.  I can do it now, no problem; it'll just take a hot minute..."));
                await PrepareAsync(origPath, pcmPath);
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Playing '{file}' now..."));
            }

            try
            {
                session.PlayMusic(pcmPath, async () => await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Done playing '{file}'.")));
            }
            catch (AlreadyPlayingMusicException)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("I'm already playing something else.  Please wait until that's done."));
            }
        }
    }

    [SlashRequireOwner]
    [SlashCommand("joe-is-lazy", "Has Gracious do the last little bit of specialness for me.")]
    public async Task JoeIsLazyCommand(
        InteractionContext ctx,
        [Option("session-id", "That magical session ID that you were given during /join and /leave.")] long sessionId)
    {
        try
        {
            await Core();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Demystify(), "An unexpected error occurred during the '/joe-is-lazy' command.");
        }

        async ValueTask Core()
        {
            GraciousConfiguration cfg = _cfg.CurrentValue;
            Dictionary<string, (string Title, int Index)> usernameToTitle = cfg.UsernameTitleMappings
                .Select((u, index) => (u, index))
                .ToDictionary(map => map.u.Username, map => (map.u.StreamTitle, map.index));

            JankStringTemplateParameters jank = new((uint)sessionId);
            string outputFilePath = jank.Resolve(cfg.OutputFile);

            ZipArchive file;
            try
            {
                file = ZipFile.OpenRead(outputFilePath);
            }
            catch (IOException)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("File not found."));
                return;
            }

            using var _ = file;
            string lazyDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Audio", $"{sessionId}");
            string origDirectoryPath = jank.Resolve(cfg.EmergencyFolder);
            Log.Information(origDirectoryPath);
            Directory.CreateDirectory(lazyDirectoryPath);

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent($"Hi lazy person!  I'm starting the process of building up that .mkv file for you, so that you can move on to other things.  Please wait while I extract the public zip file..."));
            await Task.Run(() => file.ExtractToDirectory(lazyDirectoryPath, true));
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Hi lazy person!  I'm starting the process of building up that .mkv file for you, so that you can move on to other things.  Cool, that .zip file was extracted just fine!  Now for the hard part: combining the secret stuff that we recorded *just for us* on the side..."));

            List<string> args = new()
            {
                "-y",
                "-i", Path.Combine(origDirectoryPath, "Desktop.mkv"),
            };

            string myUser = $"{ctx.User.Username}#{ctx.User.Discriminator}";
            int myUserIndex = -1;
            List<string> usernames = new();

            foreach (string extractedFile in Directory.EnumerateFiles(lazyDirectoryPath, "*.flac")
                .OrderBy(u => usernameToTitle.TryGetValue(Path.GetFileNameWithoutExtension(u), out (string Title, int Index) tup) ? tup.Index : int.MaxValue))
            {
                string username = Path.GetFileNameWithoutExtension(extractedFile);
                if (username == myUser)
                {
                    myUserIndex = usernames.Count + 1;
                }

                usernames.Add(username);
                args.Add("-i");
                args.Add(extractedFile);
            }

            args.Add("-filter_complex");
            StringBuilder filterComplexBuilder = new($"{string.Concat(Enumerable.Range(1, usernames.Count).Select(i => $"[{i}]"))}amix=inputs={usernames.Count}:normalize=0 [allAudioInputsCombined]");
            if (myUserIndex > 0)
            {
                filterComplexBuilder.Append($";[0:a][{myUserIndex}]amix=inputs=2:normalize=0 [desktopAndMe]");
            }

            args.Add($"{filterComplexBuilder}");

            args.Add("-c:v");
            args.Add("copy");

            args.Add("-map");
            args.Add("0:v");

            args.Add("-map");
            if (myUserIndex > 0)
            {
                args.Add("[desktopAndMe]");

                args.Add("-c:a:0");
                args.Add("flac");
            }
            else
            {
                args.Add("0:a");

                args.Add("-c:a:0");
                args.Add("copy");
            }

            args.Add("-metadata:s:a:0");
            args.Add("title=Desktop");

            args.Add("-map");
            args.Add("[allAudioInputsCombined]");

            args.Add("-c:a:1");
            args.Add("flac");

            args.Add("-metadata:s:a:1");
            args.Add("title=AllAudioInputsCombined");

            for (int i = 0; i < usernames.Count; i++)
            {
                args.Add("-map");
                args.Add($"{i + 1}:0");

                args.Add($"-c:a:{i + 2}");
                args.Add($"copy");
                if (usernameToTitle.TryGetValue(usernames[i], out (string Title, int Index) tup))
                {
                    args.Add($"-metadata:s:a:{i + 2}");
                    args.Add($"title={tup.Title}");
                }
            }

            string mkvPath = Path.Combine(lazyDirectoryPath, "lazy-pig.mkv");
            args.Add(mkvPath);
            using FfmpegProcessWrapper ffmpeg = FfmpegProcessWrapper.Create(args) ?? throw new InvalidOperationException("ffmpeg wrapper should not return null for non-empty arg list");
            ffmpeg.Start();
            await ffmpeg.End();

            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Hi lazy person!  I've built up that .mkv file for you.  It's at `{mkvPath}`."));
        }
    }

    private static async ValueTask<Dictionary<string, string>> ReadMusicIndexAsync(GraciousConfiguration cfg)
    {
        string musicFolder = cfg.MusicFolder;
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(musicFolder, "index.txt"));
        Dictionary<string, string> index = new(lines.Length);
        foreach (string line in lines)
        {
            string[] spl = line.Split('|', 2, StringSplitOptions.RemoveEmptyEntries);
            if (spl.Length == 2)
            {
                index.Add(spl[0], Path.Combine(musicFolder, spl[1]));
            }
        }

        return index;
    }

    private static async ValueTask PrepareAsync(string origPath, string pcmPath)
    {
        string[] args =
        {
            "-i", origPath,
            "-f", "s16le",
            "-ac", "2",
            "-ar", "48000",
        };

        {
            await using FileStream pcmFile = Files.CreateAsync(pcmPath + ".tmp");
            await using CompressionStream gpCompress = new(pcmFile, new CompressionOptions(CompressionOptions.MaxCompressionLevel));
            using var ffmpeg = FfmpegProcessWrapper.Create(args, gpCompress) ?? throw new InvalidOperationException("ffmpeg wrapper should not return null for non-empty arg list");
            ffmpeg.Start();
            await ffmpeg.End();
            await gpCompress.FlushAsync();
        }

        File.Move(pcmPath + ".tmp", pcmPath, overwrite: true);
    }
}
