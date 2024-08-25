import type { Collection } from "discord.js";
import type { ConventionalCommand } from "./commands";

declare module 'discord.js' {
    interface Client {
        commands: Collection<string, ConventionalCommand>;
    }
}
