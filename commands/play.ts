import { copyFile } from 'fs/promises';
import * as path from 'path';

import { SlashCommandBuilder, type RepliableInteraction } from 'discord.js';
import { AudioPlayerError, AudioPlayerStatus, createAudioResource, PlayerSubscription, type AudioPlayerState } from '@discordjs/voice';

import type { ConventionalCommand } from '.';

const data = new SlashCommandBuilder()
    .setName('play')
    .setDescription('Orders Gracious to start playing a music track that I have on my PC.')
    .addStringOption(o => o
        .setName('file')
        .setDescription('The name of the music file that I can stream.')
        .setRequired(true));

const execute = async (interaction: RepliableInteraction) => {
    if (!(interaction.isChatInputCommand() && interaction.guildId && interaction.guild)) {
        await interaction.reply('This can only be used from a slash command in a guild.');
        return;
    }

    if (interaction.member?.user.id !== interaction.client.application.owner?.id) {
        await interaction.reply('For the time being, only the owner is allowed to use this bot. The rest of the code hasn\'t been written yet.');
        return;
    }

    let session = interaction.client.data.sessions.get(interaction.guildId);
    if (!session) {
        await interaction.reply('Hey buddy, I\'m not in a voice channel. This is the rewrite, you\'re supposed to be gentle with me, remember?');
        return;
    }

    const trackFile = interaction.options.getString('file', true);
    const trackInfo = interaction.client.data.playableTracks.get(trackFile);
    if (!trackInfo) {
        await interaction.reply(`'${trackFile}' is not something that I know how to play.`);
        return;
    }

    if (session.player.state.status !== AudioPlayerStatus.Idle) {
        await interaction.reply('Already playing something else. I know it\'s possible (even easier after this rewrite than before), but I just don\'t feel like adding the ability to switch tracks mid-stream right now.');
        return;
    }

    const targetPath = path.join(session.dir, 'track.' + (Date.now() - session.start) + '.' + trackInfo.name);
    await copyFile(trackInfo.fullPath, targetPath);

    let subscription: PlayerSubscription | null = null;
    let ignorePendingErrorCallback = false;
    let ignorePendingStateChangeCallback = false;
    const errorCallback = async (error: AudioPlayerError) => {
        if (ignorePendingErrorCallback) {
            return;
        }

        session.player.off('error', errorCallback);
        ignorePendingErrorCallback = true;

        session.player.off('stateChange', stateChangeCallback);
        ignorePendingStateChangeCallback = true;

        await interaction.editReply(`Error trying to play '${trackFile}': ${error.toString()}.`);
    };
    session.player.on('error', errorCallback);

    const stateChangeCallback = async (oldState: AudioPlayerState, newState: AudioPlayerState) => {
        if (ignorePendingStateChangeCallback) {
            return;
        }

        if (oldState.status !== AudioPlayerStatus.Playing && newState.status === AudioPlayerStatus.Playing) {
            await interaction.reply(`Playing '${trackFile}' now...`);
        } else if (oldState.status !== AudioPlayerStatus.Idle && newState.status === AudioPlayerStatus.Idle) {
            session.player.off('stateChange', stateChangeCallback);
            ignorePendingStateChangeCallback = false;

            session.player.off('error', errorCallback);
            ignorePendingErrorCallback = true;

            subscription?.unsubscribe();
            await interaction.editReply(`Done playing '${trackFile}'.`);
        }
    };
    session.player.on('stateChange', stateChangeCallback);

    subscription = session.connection.subscribe(session.player) ?? null;
    try {
        session.player.play(createAudioResource(targetPath));
    } catch (err) {
        subscription?.unsubscribe();
        throw err;
    }
}

export default <ConventionalCommand>{ data, execute };
