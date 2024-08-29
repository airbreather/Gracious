import path from "path";

import type { AppConfig, FfmpegArg } from ".";

const ffmpeg = async (args: ReadonlyArray<FfmpegArg>) => {
    const _args = ['ffmpeg'];
    for (const arg of args) {
        if (typeof arg === 'string') {
            _args.push(arg);
        } else {
            for (const k of Object.keys(arg)) {
                _args.push(k);
                _args.push(arg[k]);
            }
        }
    }

    const ffmpegExit = await Bun.spawn(_args).exited;
    if (ffmpegExit != 0) {
        throw new Error(`'ffmpeg' exited with code ${ffmpegExit}`);
    }
}

const ffmpegSingleInputSingleOutput = ({ inputArgs, input, outputArgs, output }: { inputArgs: ReadonlyArray<FfmpegArg>, input: string, outputArgs: ReadonlyArray<FfmpegArg>, output: string }) => {
    return ffmpeg([
        ...inputArgs,
        { '-i': input },
        ...outputArgs,
        output,
    ]);
}

export const run = async (appConfig: AppConfig, workingDirectoryPath: string) => {
    const fifoPath = path.join(workingDirectoryPath, 'tmp.fifo');
    const screenPath = path.join(workingDirectoryPath, 'screenOnly.mkv');
    const mkfifoProc = Bun.spawn(['mkfifo', fifoPath]);
    const mkfifoExit = await mkfifoProc.exited;
    if (mkfifoExit != 0) {
        throw new Error(`'mkfifo' exited with code ${mkfifoExit}`);
    }

    const screenRecordProc = Bun.spawn([appConfig.recordScreenExe, fifoPath]);
    const ffmpegScreen = ffmpegSingleInputSingleOutput({
        inputArgs: [
            '-y',
            { '-use_wallclock_as_timestamps': '1' },
            { '-pix_fmt': 'bgra' },
            { '-s': '2560x1440' },
            { '-f': 'rawvideo' },
        ],
        input: fifoPath,
        outputArgs: appConfig.ffmpegArgs.realtimeVideoEncode,
        output: screenPath,
    });
    const ffmpegAudio = [] as Promise<void>[];
    const desktopAudioInputs = appConfig.ffmpegArgs.desktopAudioInputs;
    for (const inputKey of Object.keys(desktopAudioInputs)) {
        ffmpegAudio.push(ffmpegSingleInputSingleOutput({
            inputArgs: ['-y', ...desktopAudioInputs[inputKey]],
            input: inputKey,
            outputArgs: appConfig.ffmpegArgs.realtimeAudioEncode,
            output: path.join(workingDirectoryPath, `audio_${ffmpegAudio.length}.flac`),
        }));
    }

    await Promise.all([screenRecordProc, ffmpegScreen, ...ffmpegAudio]);
};
