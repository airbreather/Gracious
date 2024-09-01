import * as fsp from 'fs/promises';
import * as path from 'path';

import escapeStringRegexp from 'escape-string-regexp';

import ffmpeg from 'fluent-ffmpeg';

export const combineTracks = async (inputDirPath: string, username: string, outputDirPath: string) => {
    await fsp.mkdir(outputDirPath, { recursive: true });
    const inputDir = await fsp.opendir(inputDirPath);
    const inputFiles = new Map<number, string>();
    const matcher = new RegExp(`^${escapeStringRegexp(username)}\\.(?<startTimestamp>\\d+)\\.opus$`);
    for await (const inputFile of inputDir) {
        if (!inputFile.isFile()) {
            continue;
        }

        const timestamp = matcher.exec(inputFile.name)?.groups?.['startTimestamp'];
        if (typeof timestamp === 'string') {
            inputFiles.set(Number(timestamp), path.join(inputDirPath, inputFile.name));
        }
    }

    let ffmpegCommand = ffmpeg();
    const starts = [];
    for (const [timestamp, inputPath] of [...inputFiles.entries()].sort(([ts0, ], [ts1, ]) => ts0 - ts1)) {
        starts.push(timestamp);
        ffmpegCommand = ffmpegCommand.input(inputPath);
    }

    ffmpegCommand = ffmpegCommand
        .complexFilter(
            [...
                starts.map((ts, i) => ({
                    filter: 'adelay', options: `${ts}|${ts}`,
                    inputs: `[${i}]`, outputs: `[d${i}]`,
                })),

                {
                    filter: 'amix', options: { inputs: starts.length, normalize: 0 },
                    inputs: starts.map((_, i) => `[d${i}]`), outputs: 'o',
                }
            ], 'o')
        .outputOption('-compression_level', '12')
        .save(path.join(outputDirPath, `${username}.flac`));
};
