import type { Collection } from "discord.js";
import type { ConventionalCommand } from "./commands";
import type { AppConfig } from ".";

declare module 'discord.js' {
    interface Client {
        commands: Collection<string, ConventionalCommand>;
        appConfig: AppConfig;
    }
}
