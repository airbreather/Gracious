import path from 'path';
import * as fs from 'fs';
import type { SpawnOptions, Subprocess } from 'bun';

import type { AppConfig, FfmpegArg } from '.';

export const procs = [] as { args: ReadonlyArray<string>, proc: Subprocess, abort: AbortController }[];

const spawn = <Opts extends SpawnOptions.OptionsObject>(args: string[], options?: Opts) => {
    const proc = Bun.spawn(args, options);
    const abort = new AbortController();
    const entry = { args, proc, abort };
    procs.push(entry);
    return entry;
}

const unwrap = async <T extends Subprocess>(subprocess: { args: ReadonlyArray<string>, proc: T }) => {
    const exitCode = await subprocess.proc.exited;
    if (exitCode !== 0 && exitCode !== 130) {
        throw new Error(`Process exited with code ${exitCode}. Original args:\n${subprocess.args}`);
    }
}

const ffmpeg = (args: ReadonlyArray<FfmpegArg>) => {
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

    return spawn(_args, { stdin: 'pipe' });
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
    const screenRecordProc = spawn([appConfig.recordScreenExe, fifoPath], { env: { ...process.env, 'RUST_BACKTRACE': 'full' } });
    screenRecordProc.abort.signal.addEventListener('abort', () => screenRecordProc.proc.kill('SIGTERM'));

    await new Promise<void>(resolve => {
        const interval = setInterval(() => {
            if (fs.existsSync(fifoPath)) {
                clearInterval(interval);
                resolve();
            }
        }, 100);
    })

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
    const ffmpegAudio = [] as { args: ReadonlyArray<string>, abort: AbortController, proc: Subprocess<'pipe', 'pipe', 'inherit'> }[];
    const desktopAudioInputs = appConfig.ffmpegArgs.desktopAudioInputs;
    for (const inputKey of Object.keys(desktopAudioInputs)) {
        const ffmpegAudioProc = ffmpegSingleInputSingleOutput({
            inputArgs: ['-y', ...desktopAudioInputs[inputKey]],
            input: inputKey,
            outputArgs: appConfig.ffmpegArgs.realtimeAudioEncode,
            output: path.join(workingDirectoryPath, `audio_${ffmpegAudio.length}.flac`),
        });
        ffmpegAudioProc.abort.signal.addEventListener('abort', () => ffmpegAudioProc.proc.kill('SIGTERM'));
        ffmpegAudio.push(ffmpegAudioProc);
    }

    return async () => {
        screenRecordProc.proc.kill('SIGINT');
        procs.splice(procs.indexOf(screenRecordProc), 1);
        for (const audioProc of ffmpegAudio) {
            audioProc.proc.stdin.write('q');
        }

        await Promise.all([unwrap(screenRecordProc), unwrap(ffmpegScreen), ...ffmpegAudio.map(unwrap)]);
    };
};
