import * as fs from 'fs';
import * as path from 'path';
import * as prism from 'prism-media';
import * as stream from 'stream/promises';

import { channelMention, ChannelType, Client, GuildMember, SlashCommandBuilder, type RepliableInteraction } from 'discord.js';
import { createAudioPlayer, EndBehaviorType, getVoiceConnection, joinVoiceChannel, VoiceConnection } from '@discordjs/voice';

import type { ConventionalCommand } from '.';
import * as recordScreen from '../record-screen';
import type { GraciousStream } from '..';
import { playTrack } from '../play-track';

const data = new SlashCommandBuilder()
    .setName('join')
    .setDescription('Orders Gracious to join the listed voice channel.')
    .addChannelOption(o => o
        .setName('channel')
        .setDescription('What channel to join, or leave it blank for me to join your current one.')
        .addChannelTypes(ChannelType.GuildVoice));

const runReceiveLoop = async (guildId: string, connection: VoiceConnection, start: number, dir: string, client: Client) => {
    const activeStreams: GraciousStream[] = [];
    const screenRecording = recordScreen.run(client.data.appConfig, start, dir);
    connection.receiver.speaking.on('start', async (userId) => {
        if (client.data.sessions.get(guildId)?.stopping === true) {
            return;
        }

        const receiveStream = connection.receiver.subscribe(userId, {
            end: { behavior: EndBehaviorType.Manual },
            highWaterMark: 1 << 20, // allocate a generous 1 MiB buffer in case user fetch is slow.
        });
        const flushed = new Promise<void>(async (resolve, reject) => {
            try {
                const elapsed = Date.now() - start;
                const oggStream = new prism.opus.OggLogicalBitstream({
                    opusHead: new prism.opus.OpusHead({
                        channelCount: 2,
                        sampleRate: 48000,
                    }),
                    pageSizeControl: {
                        maxPackets: 10,
                    },
                });

                const user = await client.users.fetch(userId);
                const fileName = path.join(dir, `${user.tag}.${elapsed}.opus`);
                const file = fs.createWriteStream(fileName);
                try {
                    await stream.pipeline(receiveStream, oggStream, file);
                } catch (err) {
                    console.warn(`❌ Error recording file ${fileName} - ${err}`);
                }

                const idx = activeStreams.findIndex(a => a.receiveStream === receiveStream);
                if (idx > -1) {
                    activeStreams.splice(idx, 1);
                }

                resolve();
            } catch (err) {
                reject(err);
            }
        });
        activeStreams.push({ receiveStream, flushed });
    });

    connection.receiver.speaking.on('end', userId => {
        connection.receiver.subscriptions.get(userId)?.push(null);
    });

    const player = createAudioPlayer();
    connection.subscribe(player);
    client.data.sessions.set(guildId, {
        activeStreams,
        stopping: false,
        connection,
        player,
        start,
        dir,
        terminateGracefully: async () => {
            const session = client.data.sessions.get(guildId);
            if (!session) {
                console.warn('no session to terminate???');
                return;
            }

            session.stopping = true;
            client.data.sessions.delete(guildId);
            for (const stream of activeStreams) {
                stream.receiveStream.push(null);
            }

            await Promise.all([(await screenRecording)(), ...session.activeStreams.map(s => s.flushed)]);
        },
    });

    await playTrack(
        player,
        connection,
        dir,
        start,
        'now-recording',
        client.data.playableTracks,
        () => Promise.resolve(),
        () => Promise.resolve(),
    );
}

const execute = async (interaction: RepliableInteraction) => {
    if (!(interaction.isChatInputCommand() && interaction.guildId && interaction.guild)) {
        await interaction.reply('This can only be used from a slash command in a guild.');
        return;
    }

    if (interaction.member?.user.id !== interaction.client.application.owner?.id) {
        await interaction.reply('For the time being, only the owner is allowed to use this bot. The rest of the code hasn\'t been written yet.');
        return;
    }

    let targetChannel = interaction.options.getChannel('channel');
    if (!targetChannel && interaction.member instanceof GuildMember) {
        targetChannel = interaction.member.voice.channel;
    }

    if (!targetChannel) {
        await interaction.reply('Channel can only be blank if you\'re already in a channel. Otherwise, how am I supposed to know where to go?');
        return;
    }

    let connection = getVoiceConnection(interaction.guildId);
    let currentChannel = connection && (await interaction.guild.voiceStates.fetch('@me')).channel;

    if (!connection || currentChannel?.id !== targetChannel.id) {
        connection = joinVoiceChannel({
            channelId: targetChannel.id,
            guildId: interaction.guild.id,
            selfDeaf: false,
            selfMute: false,
            debug: true,
            adapterCreator: interaction.guild.voiceAdapterCreator,
        });
    }

    const start = Date.now();
    const dir = path.join(interaction.client.data.appConfig.workingDirectoryPathBase, `${start}`);
    fs.mkdirSync(dir);
    if (targetChannel) {
        await interaction.reply(`Started recording in channel ${channelMention(targetChannel.id)}. Magic number: ${'`'}${start}${'`'}`);
    } else {
        await interaction.reply(`Started recording. Magic number: ${'`'}${start}${'`'}`);
    }

    runReceiveLoop(interaction.guildId, connection, start, dir, interaction.client);
}

export default <ConventionalCommand>{ data, execute };
