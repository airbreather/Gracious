import { ChannelType, SlashCommandBuilder, type RepliableInteraction } from "discord.js";
import type { ConventionalCommand } from ".";

const data = new SlashCommandBuilder()
    .setName('join')
    .setDescription('Orders Gracious to join the listed voice channel.')
    .addChannelOption(o => o
        .setName('channel')
        .setDescription('What channel to join, or leave it blank for me to join your current one.')
        .addChannelTypes(ChannelType.GuildVoice));

const execute = async (interaction: RepliableInteraction) => {
    await interaction.reply(`This is where I would join, on account of ${interaction.user.username} having asked me to do so.`);
}

export default <ConventionalCommand>{ data, execute };
