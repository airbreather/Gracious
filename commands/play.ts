import { SlashCommandBuilder, type RepliableInteraction } from 'discord.js';

import type { ConventionalCommand } from '.';
import { Result as PlayTrackResult, playTrack } from '../play-track';

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
    const playTrackResult = playTrack(
        session.player,
        session.connection,
        session.dir,
        trackFile,
        interaction.client.data.playableTracks,
        () => interaction.reply(`Playing '${trackFile}' now...`),
        (reason) => interaction.editReply(reason),
    );
    switch (await playTrackResult) {
        case PlayTrackResult.Success:
            return;

        case PlayTrackResult.AlreadyPlayingSomethingElse:
            await interaction.reply('Already playing something else. I know it\'s possible (even easier after this rewrite than before), but I just don\'t feel like adding the ability to switch tracks mid-stream right now.');
            return;

        case PlayTrackResult.UnrecognizedTrack:
            await interaction.reply(`'${trackFile}' is not something that I know how to play.`);
            return;
    }
}

export default <ConventionalCommand>{ data, execute };
