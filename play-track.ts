import * as fsp from 'fs/promises';
import * as path from 'path';

import type { Collection } from "discord.js";
import { AudioPlayerError, AudioPlayerStatus, createAudioResource, PlayerSubscription, VoiceConnection, type AudioPlayer, type AudioPlayerState } from "@discordjs/voice";

export const enum Result {
    Success,
    UnrecognizedTrack,
    AlreadyPlayingSomethingElse,
}

export const playTrack = async (
    player: AudioPlayer,
    connection: VoiceConnection,
    dir: string,
    trackFile: string,
    playableTracks: Collection<string, { name: string, fullPath: string }>,
    begin: () => Promise<unknown>,
    done: (reason: string) => Promise<unknown>,
) => {
    const trackInfo = playableTracks.get(trackFile);
    if (!trackInfo) {
        return Result.UnrecognizedTrack;
    }

    if (player.state.status !== AudioPlayerStatus.Idle) {
        return Result.AlreadyPlayingSomethingElse;
    }

    let subscription: PlayerSubscription | null = null;
    let ignorePendingErrorCallback = false;
    let ignorePendingStateChangeCallback = false;
    const errorCallback = async (error: AudioPlayerError) => {
        if (ignorePendingErrorCallback) {
            return;
        }

        player.off('error', errorCallback);
        ignorePendingErrorCallback = true;

        player.off('stateChange', stateChangeCallback);
        ignorePendingStateChangeCallback = true;

        await done(`Error trying to play '${trackFile}': ${error.toString()}.`);
    };
    player.on('error', errorCallback);

    const stateChangeCallback = async (oldState: AudioPlayerState, newState: AudioPlayerState) => {
        if (ignorePendingStateChangeCallback) {
            return;
        }

        if (oldState.status !== AudioPlayerStatus.Playing && newState.status === AudioPlayerStatus.Playing) {
            await begin();
        } else if (oldState.status !== AudioPlayerStatus.Idle && newState.status === AudioPlayerStatus.Idle) {
            player.off('stateChange', stateChangeCallback);
            ignorePendingStateChangeCallback = false;

            player.off('error', errorCallback);
            ignorePendingErrorCallback = true;

            subscription?.unsubscribe();
            await done(`Done playing '${trackFile}'.`);
        }
    };
    player.on('stateChange', stateChangeCallback);

    const targetPath = path.join(dir, `track.${Date.now()}.${trackInfo.name}`);
    const copyPromise = fsp.copyFile(trackInfo.fullPath, targetPath);
    subscription = connection.subscribe(player) ?? null;
    try {
        player.play(createAudioResource(trackInfo.fullPath));
    } catch (err) {
        subscription?.unsubscribe();
        throw err;
    }

    await copyPromise;
    return Result.Success;
}