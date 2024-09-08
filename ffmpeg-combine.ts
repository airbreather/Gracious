import * as fsp from 'fs/promises';
import * as path from 'path';
import { $ } from 'bun';

import escapeStringRegexp from 'escape-string-regexp';

const pcmBytesToMilliseconds = 1000 /* seconds per millisecond */ / ((48000 * 2) /* stereo samples per second */ * 2 /* bytes per sample */);

export const combineTracks = async (inputDirPath: string, username: string, outputDirPath: string) => {
    const inputDir = await fsp.opendir(inputDirPath);
    const inputFiles = new Map<number, string>();
    const currentUserMatcher = new RegExp(`^${escapeStringRegexp(username)}\\.(?<startTimestamp>\\d+)\\.opus$`);
    for await (const inputFile of inputDir) {
        if (!inputFile.isFile()) {
            continue;
        }

        const timestamp = currentUserMatcher.exec(inputFile.name)?.groups?.['startTimestamp'];
        if (typeof timestamp === 'string') {
            inputFiles.set(Number(timestamp), path.join(inputDirPath, inputFile.name));
        }
    }

    const silenceDirPath = path.join(outputDirPath, 'silence-samples');
    await fsp.mkdir(silenceDirPath, { recursive: true });
    const silenceMatcher = new RegExp(`^silence\\.(?<silenceDuration>\\d+)\\.opus$`);
    const silenceDurations = new Set<number>();
    const silenceDir = await fsp.opendir(silenceDirPath);
    const concatFilePath = path.join(outputDirPath, `concat-${username}.txt`);
    const concatFile = Bun.file(concatFilePath).writer(); // don't bother with async, I can't figure it out quickly and it doesn't matter enough.
    for await (const outputFile of silenceDir) {
        if (!outputFile.isFile()) {
            continue;
        }

        const silenceDuration = silenceMatcher.exec(outputFile.name)?.groups?.['silenceDuration'];
        if (typeof silenceDuration === 'string') {
            silenceDurations.add(Number(silenceDuration));
        }
    }

    const silenceCommands = [] as Promise<number>[];
    let prevTimestamp = 0;
    for (const [timestamp, inputPath] of [...inputFiles.entries()].sort(([ts0, ], [ts1, ]) => ts0 - ts1)) {
        // timestamp is the START of this clip, but our silence needs to start at the END of it.
        let pcmBytes = 0;
        try {
            pcmBytes = Number(await $`opusdec ${inputPath} - | wc -c`.text());
        } catch (err) {
            // this has been observed with at least one clip that didn't get fully written.
            continue;
        }

        const inputDuration = Number(pcmBytes * pcmBytesToMilliseconds);

        const silenceDuration = timestamp - prevTimestamp;
        const silenceFilePath = path.join(silenceDirPath, `silence.${silenceDuration}.opus`);
        if (!silenceDurations.has(silenceDuration)) {
            silenceDurations.add(silenceDuration);
            silenceCommands.push(Bun.spawn([
                'ffmpeg',
                '-f', 'lavfi',
                '-i', `anullsrc=channel_layout=stereo:sample_rate=48000:duration=${(silenceDuration / 1000).toFixed(3)}`,
                '-b', '6K',
                silenceFilePath,
            ], { stdio: ['ignore', 'ignore', 'ignore'] }).exited);
        }

        concatFile.write(`file '${silenceFilePath}'\n`);
        concatFile.write(`duration ${(silenceDuration / 1000).toFixed(3)}\n`);
        concatFile.write(`file '${inputPath}'\n`);
        concatFile.write(`duration ${(inputDuration / 1000).toFixed(3)}\n`);

        prevTimestamp = timestamp + inputDuration;
    }

    await concatFile.end();
    await Promise.all(silenceCommands);

    await Bun.spawn([
        'ffmpeg',
        '-y',
        '-f', 'concat',
        '-safe', '0',
        '-segment_time_metadata', '1',
        '-i', concatFilePath,
        '-c', 'copy',
        path.join(outputDirPath, `${username}.opus`),
    ], { stdio: ['ignore', 'ignore', 'ignore'] }).exited;
};
