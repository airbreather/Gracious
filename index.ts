import { Client, Collection, Events, GatewayIntentBits } from 'discord.js';
import * as os from 'os';
import * as path from 'path';
import * as yaml from 'yaml';

import { allCommands } from './commands';
import deployCommands from './deploy-commands';

type SingleStringProperty = Readonly<{ [key: string]: string } & Record<string, never>>;
type FfmpegArg = string | SingleStringProperty;

interface FfmpegArgs {
    desktopAudioInputs: Readonly<Record<string, ReadonlyArray<FfmpegArg>>>;
    realtimeAudioEncode: ReadonlyArray<FfmpegArg>;
    offlineAudioEncode: ReadonlyArray<FfmpegArg>;
    realtimeVideoEncode: ReadonlyArray<FfmpegArg>;
    offlineVideoEncode: ReadonlyArray<FfmpegArg>;
}

export interface AppConfig {
    applicationId: string;
    botToken: string;
    workingDirectoryPathBase: string;
    musicDirectoryPath: string;
    guildIdsForApplicationCommands: ReadonlyArray<string>;
    usernameTitleMappings: ({ [key: string]: string } & Record<string, never>)[];
    ffmpegArgs: FfmpegArgs;
}

const appConfig: AppConfig = yaml.parse(await Bun.file(path.join(os.homedir(), 'secret-discord-config.yml')).text());

const deployCommandsPromise = deployCommands(appConfig);

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

await deployCommandsPromise;
client.login(appConfig.botToken);
