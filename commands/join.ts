import * as fs from 'node:fs';
import * as stream from 'node:stream/promises';
import * as path from 'path';
import * as prism from 'prism-media';

import { ChannelType, Client, GuildMember, SlashCommandBuilder, type RepliableInteraction } from 'discord.js';
import { EndBehaviorType, getVoiceConnection, joinVoiceChannel, VoiceConnection } from '@discordjs/voice';

import type { ConventionalCommand } from '.';

const data = new SlashCommandBuilder()
    .setName('join')
    .setDescription('Orders Gracious to join the listed voice channel.')
    .addChannelOption(o => o
        .setName('channel')
        .setDescription('What channel to join, or leave it blank for me to join your current one.')
        .addChannelTypes(ChannelType.GuildVoice));

const runReceiveLoop = async (guildId: string, connection: VoiceConnection, start: number, dir: string, client: Client) => {
    let { voiceConnections } = client.data;
    voiceConnections.set(guildId, connection);
    connection.receiver.speaking.on('start', async (userId) => {
        const receiveStream = connection.receiver.subscribe(userId, {
            end: { behavior: EndBehaviorType.Manual },
        });
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
        const fileName = path.join(dir, `${user.username}.${elapsed}.opus`);
        const file = fs.createWriteStream(fileName);
        try {
            await stream.pipeline(receiveStream, oggStream, file);
        } catch (err) {
            console.warn(`❌ Error recording file ${fileName} - ${err}`);
        }
    });

    connection.receiver.speaking.on('end', userId => {
        connection.receiver.subscriptions.get(userId)?.push(null);
    });
}

const execute = async (interaction: RepliableInteraction) => {
    if (!(interaction.isChatInputCommand() && interaction.guildId && interaction.guild)) {
        await interaction.reply('This can only be used from a slash command in a guild.');
        return;
    }

    let connection = interaction.guildId && getVoiceConnection(interaction.guildId);
    if (!connection) {
        let channel = interaction.options.getChannel('channel');
        if (!channel && interaction.member instanceof GuildMember) {
            channel = interaction.member.voice.channel;
        }

        if (!channel) {
            await interaction.reply('Channel can only be blank if you\'re already in a channel. Otherwise, I don\'t know where to go!');
            return;
        }

        connection = joinVoiceChannel({
            channelId: channel.id,
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
    await interaction.reply(`Joined <#${connection.joinConfig.channelId}>. Magic number: ${'`'}${start}${'`'}`);
    runReceiveLoop(interaction.guildId, connection, start, dir, interaction.client);
}

export default <ConventionalCommand>{ data, execute };
