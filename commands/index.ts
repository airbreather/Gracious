import type { RepliableInteraction, SlashCommandBuilder } from 'discord.js';

import join from './join';
import leave from './leave';
import play from './play';

export interface ConventionalCommand {
    data: SlashCommandBuilder,
    execute: (interaction: RepliableInteraction) => Promise<void>,
}

export const allCommands: Record<string, ConventionalCommand> = {
    join, leave, play,
};
