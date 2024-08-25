import { Client, Collection, Events, GatewayIntentBits, REST, Routes } from 'discord.js';
import * as os from 'os';
import * as path from 'path';
import * as yaml from 'yaml';

import { allCommands } from './commands';

type SingleStringProperty = Readonly<{ [key: string]: string } & Record<string, never>>;
type FfmpegArg = string | SingleStringProperty;

interface FfmpegArgs {
    desktopAudioInputs: Readonly<Record<string, ReadonlyArray<FfmpegArg>>>;
    realtimeAudioEncode: ReadonlyArray<FfmpegArg>;
    offlineAudioEncode: ReadonlyArray<FfmpegArg>;
    realtimeVideoEncode: ReadonlyArray<FfmpegArg>;
    offlineVideoEncode: ReadonlyArray<FfmpegArg>;
}

interface AppConfig {
    applicationId: string;
    botToken: string;
    workingDirectoryPathBase: string;
    musicDirectoryPath: string;
    guildIdsForApplicationCommands: ReadonlyArray<string>;
    usernameTitleMappings: ({ [key: string]: string } & Record<string, never>)[];
    ffmpegArgs: FfmpegArgs;
}

const appConfig: AppConfig = yaml.parse(await Bun.file(path.join(os.homedir(), 'secret-discord-config.yml')).text());

const rest = new REST().setToken(appConfig.botToken);

try {
    console.log(`Started refreshing ${allCommands.length} application (/) commands.`);

    for (const guildId of appConfig.guildIdsForApplicationCommands) {
        const data = await rest.put(
            Routes.applicationGuildCommands(`${appConfig.applicationId}`, `${guildId}`),
            { body: Object.values(allCommands).map(c => c.data.toJSON()) },
        );

        if (typeof data === 'object' && data && 'length' in data) {
            console.log(`Successfully reloaded ${data.length} application (/) command(s) on guild ${guildId}.`);
        } else {
            console.warn('Check the discord.js docs for any updates, because this would be a type error.');
        }
    }
} catch (error) {
    console.error(error);
}

const client = new Client({ intents: [GatewayIntentBits.GuildVoiceStates] });

client.commands = new Collection();
for (const [k, v] of Object.entries(allCommands)) {
    client.commands.set(k, v);
}

client.once(Events.ClientReady, readyClient => {
    console.log(`Ready! Logged in as ${readyClient.user.tag}`);
});

client.on(Events.InteractionCreate, async interaction => {
    if (!interaction.isChatInputCommand()) return;

    const command = interaction.client.commands.get(interaction.commandName);

    if (!command) {
        console.error(`No command matching ${interaction.commandName} was found.`);
        return;
    }

    try {
        await command.execute(interaction);
    } catch (error) {
        console.error(error);
        if (interaction.replied || interaction.deferred) {
            await interaction.followUp({ content: 'There was an error while executing this command!', ephemeral: true });
        } else {
            await interaction.reply({ content: 'There was an error while executing this command!', ephemeral: true });
        }
    }
});

client.login(appConfig.botToken);
