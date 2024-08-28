import { channelMention, SlashCommandBuilder, type RepliableInteraction } from 'discord.js';
import { getVoiceConnection } from '@discordjs/voice';

import type { ConventionalCommand } from '.';

const data = new SlashCommandBuilder()
    .setName('leave')
    .setDescription('Orders Gracious to leave its current channel and finish recording.');

const execute = async (interaction: RepliableInteraction) => {
    if (!(interaction.isChatInputCommand() && interaction.guildId && interaction.guild)) {
        await interaction.reply('This can only be used from a slash command in a guild.');
        return;
    }

    if (interaction.member?.user.id !== interaction.client.application.owner?.id) {
        await interaction.reply('For the time being, only the owner is allowed to use this bot. The rest of the code hasn\'t been written yet.');
        return;
    }

    let connection = getVoiceConnection(interaction.guildId);
    if (!connection) {
        await interaction.reply('Hey buddy, I\'m not in a voice channel. This is the rewrite, you\'re supposed to be gentle with me, remember?');
        return;
    }

    const data = interaction.client.data;
    data.stopping = true;
    try {
        await Promise.all(data.activeStreams.map(async s => {
            s.receiveStream.push(null);
            await s.flushed;
        }));
    } finally {
        data.stopping = false;
    }

    const channel = (await interaction.guild.voiceStates.fetch('@me')).channel;
    const disconnected = connection.disconnect();
    if (disconnected) {
        if (channel) {
            await interaction.reply(`Stopped recording in ${channelMention(channel.id)}.`);
        } else {
            await interaction.reply(`I'm not **quite** sure which channel I just left, but the Discord gods tell me that I just successfully disconnected from a voice connection in ${interaction.guild.name}`);
        }
    } else {
        if (channel) {
            await interaction.reply(`Failed to stop recording in ${channelMention(channel.id)}.`);
        } else {
            await interaction.reply(`I'm not **quite** sure which channel I just tried to stop recording in, but I definitely failed to leave it, and the Discord gods tell me that I was probably in one (yes, in ${interaction.guild.name}), so I don't know what to believe.`);
        }
    }
}

export default <ConventionalCommand>{ data, execute };
