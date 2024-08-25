import type { RepliableInteraction, SlashCommandBuilder } from "discord.js";

import join from "./join";

export interface ConventionalCommand {
    data: SlashCommandBuilder,
    execute: (interaction: RepliableInteraction) => Promise<void>,
}

export const allCommands: Record<string, ConventionalCommand> = {
    join,
};
