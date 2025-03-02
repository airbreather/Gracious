import * as fsp from 'fs/promises';
import * as path from 'path';
import { $ } from 'bun';

import escapeStringRegexp from 'escape-string-regexp';

// 1000 seconds per millisecond / ((48000 * 2) stereo samples per second * 2 bytes per sample)
const pcmBytesToMilliseconds = 1000 / ((48000 * 2) * 2);

// ((48000 * 2) stereo samples per second * 2 bytes per sample) / 1000 seconds per millisecond
const millisecondsToPcmBytes = ((48000 * 2) * 2) / 1000;

const getDurationOfOpusFile = async (inputPath: string) => {
    let pcmBytes = 0;
    try {
        pcmBytes = Number(await $`opusdec ${inputPath} - | wc -c`.text());
    } catch (err) {
        return null;
    }

    return { pcmBytes, milliseconds: Number(pcmBytes * pcmBytesToMilliseconds) };
}

export const combineTracks = async (inputDirPath: string, username: string, outputDirPath: string) => {
    const inputDir = await fsp.opendir(inputDirPath);
    await fsp.mkdir(outputDirPath, { recursive: true });
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

    let hasNegativeSilences = false;
    let prevTimestamp = Number(path.basename(inputDirPath));
    const ranges = new Map<number, Record<'precedingSilence'|'inputDuration'|'endTimestamp', number>>();
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
        const inputDuration = await getDurationOfOpusFile(inputPath);
        if (inputDuration === null) {
            // this was observed with one clip that didn't get fully written. exactly WHY it didn't
            // get fully written is unknown, but again almost by definition, there's really nothing
            // that can be done in these cases. it's also not CRITICAL to keep EVERY clip (the fully
            // merged and pre-balanced desktop audio is the primary track), and as long as these are
            // VERY RARE, then I really can't justify investigating any more than it takes to deduce
            // that they are in fact VERY RARE (observed: 1/~4000 at the time of writing).
            continue;
        }

        const silenceDuration = startTimestamp - prevTimestamp;
        if (silenceDuration < 0 || (silenceDuration % 1) != 0) {
            console.error('MANUAL EFFORT NEEDED. START:', startTimestamp, 'PREV:', prevTimestamp, username);
            hasNegativeSilences = true;
        }

        const silenceFilePath = path.join(silenceDirPath, `silence.${silenceDuration}.opus`);
        const silenceFilePathTmp = path.join(silenceDirPath, `silence.${silenceDuration}.${username}-tmp.opus`);
        while (true) {
            if (silenceDurations.has(silenceDuration)) {
                const inferredSilenceDuration = await getDurationOfOpusFile(silenceFilePath);
                if (silenceDurations.has(silenceDuration)) {
                    if (inferredSilenceDuration?.pcmBytes === silenceDuration * millisecondsToPcmBytes) {
                        break;
                    }

                    console.error(`silence duration file for ${silenceDuration} mismatch, somehow.`);
                    silenceDurations.delete(silenceDuration);
                    await fsp.rm(silenceFilePath);
                }
            }

            if (silenceDuration <= 0 || silenceDuration % 1 != 0) {
                break;
            }

            await Bun.spawn([
                'ffmpeg',
                '-y',
                '-f', 'lavfi',
                '-i', `anullsrc=channel_layout=stereo:sample_rate=48000:duration=${(silenceDuration / 1000).toFixed(3)}`,
                '-b:a', '6K',
                silenceFilePathTmp,
            ], { stdio: ['ignore', 'ignore', 'ignore'] }).exited;
            if (silenceDurations.has(silenceDuration)) {
                await fsp.rm(silenceFilePathTmp);
            } else {
                silenceDurations.add(silenceDuration);
                await fsp.rename(silenceFilePathTmp, silenceFilePath);
            }
        }

        if (silenceDuration != 0) {
            concatFile.write(`file '${silenceFilePath}'\nduration ${(silenceDuration / 1000).toFixed(3)}\nfile '${inputPath}'\nduration ${(inputDuration.milliseconds / 1000).toFixed(3)}\n`);
            prevTimestamp = startTimestamp + inputDuration.milliseconds;
        }

        ranges.set(startTimestamp, { precedingSilence: silenceDuration, inputDuration: inputDuration.milliseconds, endTimestamp: startTimestamp + inputDuration.milliseconds });
    }

    await concatFile.end();

    await fsp.writeFile(path.join(outputDirPath, `${username}-segments.json`), JSON.stringify([...ranges.entries()].map(([k, v]) => ({ startTimestamp: k, ...v }))));

    if (!hasNegativeSilences) {
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
    }

    return !hasNegativeSilences;
};

export const getMetadata = async (overallStartTimestamp: number, inputDirPath: string) => {
    const inputDir = await fsp.opendir(inputDirPath);
    const audioMatcher = new RegExp(`^audio\\.(?<startTimestamp>\\d+)\\.flac$`);
    const screenMatcher = new RegExp(`^screen\\.(?<startTimestamp>\\d+)\\.mkv$`);
    let audioTimestamp = null as number | null;
    let screenTimestamp = null as number | null;
    for await (const inputFile of inputDir) {
        if (!inputFile.isFile()) {
            continue;
        }

        const maybeAudioTimestamp = audioMatcher.exec(inputFile.name)?.groups?.['startTimestamp'];
        const maybeScreenTimestamp = screenMatcher.exec(inputFile.name)?.groups?.['startTimestamp'];
        if (typeof maybeAudioTimestamp === 'string') {
            audioTimestamp = Number(maybeAudioTimestamp);
        } else if (typeof maybeScreenTimestamp === 'string') {
            screenTimestamp = Number(maybeScreenTimestamp);
        } else {
            continue;
        }

        if (audioTimestamp && screenTimestamp) {
            return {
                audioTimestamp,
                screenTimestamp,
                desktopAudioOffset: (screenTimestamp - audioTimestamp) / 1000,
                discordAudioOffset: (screenTimestamp - overallStartTimestamp) / 1000,
            }
        }
    }

    return null;
};

export interface User {
    username: string;
    title: string;
}
export const fullProcess = async (inputDirPath: string, overallStartTimestamp: number, users: ReadonlyArray<User>, outputDirPath: string, ) => {
    const combineResults = await Promise.all(users.map(({ username }) => combineTracks(inputDirPath, username, outputDirPath)));
    if (combineResults.every(x => x)) {
        const metadata = await getMetadata(overallStartTimestamp, inputDirPath);
        if (metadata) {
            const parts = [
                'ffmpeg',
                `-i ${path.join(inputDirPath, `screen.${metadata.screenTimestamp}.mkv`)}`,
                `-ss ${metadata.desktopAudioOffset.toFixed(3)} -i ${path.join(inputDirPath, `audio.${metadata.audioTimestamp}.flac`)}`,
                ...users.map(({ username }) =>
                    `-ss ${metadata.discordAudioOffset.toFixed(3)} -i ${path.join(outputDirPath, `${username}.opus`)}`
                ),
                '-map 0 -c:v libsvtav1 -crf:v 26 -preset:v 6 -svtav1-params tune=3 -r 144 -video_track_timescale 144',
                '-map 1 -c:a copy -metadata:s:a:0 title=Desktop',
                ...users.map(({ title }, index) =>
                    `-map ${index + 2} -metadata:s:a:${index + 1} title=${title}`
                ),
                path.join(outputDirPath, 'processed.mp4'),
            ];
            console.log(parts.join(' '));
            if (metadata.desktopAudioOffset < 0) {
                console.error('MANUAL EFFORT NEEDED. Desktop audio started before screen recording.')
            }
        } else {
            console.log('Failed to find audio.*.flac and/or screen.*.mkv');
        }
    } else {
        console.log('Not calculating offsets right now. Handle the reported manual interventions first.');
    }
}
