import { REST, Routes } from 'discord.js';

import type { AppConfig } from '.';
import { allCommands } from './commands';

const body = Object.values(allCommands).map(c => c.data.toJSON());

export default async (appConfig: AppConfig) => {
    const rest = new REST().setToken(appConfig.botToken);

    try {
        console.log('Started refreshing application (/) commands.');

        for (const guildId of appConfig.guildIdsForApplicationCommands) {
            const data = await rest.put(
                Routes.applicationGuildCommands(appConfig.applicationId, guildId),
                { body },
            );

            if (typeof data === 'object' && data && 'length' in data) {
                console.log(`Successfully reloaded ${data.length} application (/) command(s) on guild ${guildId}.`);
            } else {
                console.warn('Check the discord.js docs for any updates, because this would be a type error.');
            }
        }

        await rest.put(
            Routes.applicationCommands(appConfig.applicationId),
            { body },
        );
    } catch (error) {
        console.error(error);
    }
};
