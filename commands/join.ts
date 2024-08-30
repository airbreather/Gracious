import * as fs from 'node:fs';
import * as stream from 'node:stream/promises';
import * as path from 'path';
import * as prism from 'prism-media';

import { ChannelType, Client, GuildMember, SlashCommandBuilder, User, type RepliableInteraction } from 'discord.js';
import { EndBehaviorType, getVoiceConnection, joinVoiceChannel, VoiceConnection } from '@discordjs/voice';

import type { ConventionalCommand } from '.';

const data = new SlashCommandBuilder()
    .setName('join')
    .setDescription('Orders Gracious to join the listed voice channel.')
    .addChannelOption(o => o
        .setName('channel')
        .setDescription('What channel to join, or leave it blank for me to join your current one.')
        .addChannelTypes(ChannelType.GuildVoice));

const getDisplayName = (userId: string, user?: User) => {
    return user ? `${user.username}_${user.discriminator}` : userId;
};

const loopsRunning = new Map<string, VoiceConnection>();

const runReceiveLoop = async (guildId: string, connection: VoiceConnection, client: Client) => {
    if (loopsRunning.get(guildId) === connection) {
        return;
    }

    loopsRunning.set(guildId, connection);
    connection.receiver.speaking.on('start', async (userId) => {
        const receiveStream = connection.receiver.subscribe(userId, {
            end: {
                behavior: EndBehaviorType.AfterSilence,
                duration: 1000,
            },
        });

        const oggStream = new prism.opus.OggLogicalBitstream({
            opusHead: new prism.opus.OpusHead({
                channelCount: 2,
                sampleRate: 48000,
            }),
            pageSizeControl: {
                maxPackets: 10,
            },
        });

        const fileName = path.join(client.appConfig.workingDirectoryPathBase, `${Date.now()}-${getDisplayName(userId, client.users.cache.get(userId))}.opus`);
        const file = fs.createWriteStream(fileName);
        try {
            await stream.pipeline(receiveStream, oggStream, file);
            console.log(`✅ Recorded ${fileName}`);
        } catch (err) {
            console.warn(`❌ Error recording file ${fileName} - ${err}`);
        }
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

        await interaction.reply(`Joined <#${channel.id}>`);
    }

    runReceiveLoop(interaction.guildId, connection, interaction.client);
}

export default <ConventionalCommand>{ data, execute };
