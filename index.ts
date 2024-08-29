import * as os from 'os';
import * as path from 'path';
import * as yaml from 'yaml';

import { Client, Collection, Events, GatewayIntentBits, User } from 'discord.js';
import type { AudioReceiveStream } from '@discordjs/voice';

import { allCommands, type ConventionalCommand } from './commands';
import deployCommands from './deploy-commands';

export type FfmpegArg = string | Readonly<Record<string, string>>;
export interface FfmpegArgs {
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
    recordScreenExe: string;
    guildIdsForApplicationCommands: ReadonlyArray<string>;
    usernameTitleMappings: ReadonlyArray<Readonly<Record<string, string>>>;
    ffmpegArgs: FfmpegArgs;
}

const appConfig: AppConfig = yaml.parse(await Bun.file(path.join(os.homedir(), 'secret-discord-config.yml')).text());

await deployCommands(appConfig);

const client = new Client({ intents: [GatewayIntentBits.GuildVoiceStates, GatewayIntentBits.Guilds] });

const data = {
    appConfig,
    commands: new Collection<string, ConventionalCommand>(),
    stopping: false,
    activeStreams: [] as { receiveStream: AudioReceiveStream, flushed: Promise<void> }[],
};

declare module 'discord.js' { interface Client { data: typeof data; } };
client.data = data;

for (const [k, v] of Object.entries(allCommands)) {
    client.data.commands.set(k, v);
}

client.once(Events.ClientReady, async readyClient => {
    const app = await readyClient.application.fetch();
    if (app.owner instanceof User) {
        console.info(`Owner: ${app.owner.username}`);
    } else {
        console.warn(`No owner detected! Nobody will be able to actually use the bot! ${readyClient.application.owner}`);
    }

    console.log(`Ready! Logged in as ${readyClient.user.tag}`);
});

client.on(Events.InteractionCreate, async interaction => {
    if (!interaction.isChatInputCommand()) return;

    const command = interaction.client.data.commands.get(interaction.commandName);

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

client.on(Events.Error, (err) => console.error(err));

await client.login(appConfig.botToken);

process.on('SIGINT', async () => {
    await client.destroy();
});
