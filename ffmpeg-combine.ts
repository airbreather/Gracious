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

    const silenceDirPath = '/tmp/silence-samples';
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
    let prevTimestamp = Number(path.basename(inputDirPath));
    for (const [startTimestamp, inputPath] of [...inputFiles.entries()].sort(([ts0, ], [ts1, ]) => ts0 - ts1)) {
        // timestamp is the START of this clip, but our silence needs to start at the END of it. in
        // order for this to work, we need accurate duration calculations. for whatever reason, the
        // calculations based on metadata seem to be off by a significant margin, which is a problem
        // when accumulating a bunch of these individual files over a period of hours. so instead,
        // fully decode each Opus file, count the bytes, and convert that to a count of PCM samples.
        // almost by definition, this is as accurate as we can possibly get (which is to say that if
        // I'm wrong and it's still not perfect in some way, at least it's wrong in a way that will
        // transfer to the final stream and reset itself every time the next clip starts, since the
        // clips are tagged with their absolute starting timestamps anyway).
        let pcmBytes = 0;
        try {
            pcmBytes = Number(await $`opusdec ${inputPath} - | wc -c`.text());
        } catch (err) {
            // this was observed with one clip that didn't get fully written. exactly WHY it didn't
            // get fully written is unknown, but again almost by definition, there's really nothing
            // that can be done in these cases. it's also not CRITICAL to keep EVERY clip (the fully
            // merged and pre-balanced desktop audio is the primary track), and as long as these are
            // VERY RARE, then I really can't justify investigating any more than it takes to deduce
            // that they are in fact VERY RARE (observed: 1/~4000 at the time of writing).
            continue;
        }

        const inputDuration = Number(pcmBytes * pcmBytesToMilliseconds);

        const silenceDuration = startTimestamp - prevTimestamp;
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

        concatFile.write(`file '${silenceFilePath}'\nduration ${(silenceDuration / 1000).toFixed(3)}\nfile '${inputPath}'\nduration ${(inputDuration / 1000).toFixed(3)}\n`);
        prevTimestamp = startTimestamp + inputDuration;
    }

    await Promise.all([concatFile.end(), ...silenceCommands]);

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

    await fsp.rm(concatFilePath);
};
