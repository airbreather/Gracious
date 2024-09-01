import * as fs from 'fs';
import * as fsp from 'fs/promises';
import * as path from 'path';

import type { Subprocess } from 'bun';

import type { AppConfig } from '.';

export const procs = [] as { args: ReadonlyArray<string>, proc: Subprocess, abort: AbortController }[];

const unwrap = async <T extends Subprocess>(subprocess: T) => {
    const exitCode = await subprocess.exited;
    if (exitCode !== 0 && exitCode !== 130) {
        throw new Error(`Process exited with code ${exitCode}`);
    }
}

const ffmpegRedux = ({ inputArgs, input, outputArgs, output }: { inputArgs: ReadonlyArray<string>, input: string, outputArgs: ReadonlyArray<string>, output: string }) => {
    return Bun.spawn([
        'ffmpeg',
        ...inputArgs,
        '-i', input,
        ...outputArgs,
        output,
    ], { stdin: 'pipe' });
}

export const run = async (appConfig: AppConfig, start: number, workingDirectoryPath: string) => {
    const fifoPath = path.join(workingDirectoryPath, 'tmp.fifo');
    const screenRecordProc = Bun.spawn([appConfig.recordScreenExe, fifoPath], { env: { ...process.env, 'RUST_BACKTRACE': 'full' } });

    await new Promise<void>(resolve => {
        const interval = setInterval(() => {
            if (fs.existsSync(fifoPath)) {
                clearInterval(interval);
                resolve();
            }
        }, 100);
    })

    const screenPath = path.join(workingDirectoryPath, `screenOnly.${Date.now() - start}.mkv`);
    const ffmpegScreen = ffmpegRedux({
        inputArgs: [
            '-y',
            '-use_wallclock_as_timestamps', '1',
            '-pix_fmt', 'bgra',
            '-s', '2560x1368',
            '-f', 'rawvideo',
        ],
        input: fifoPath,
        outputArgs: [
            '-c', 'libx264rgb',
            '-crf', '0',
            '-filter', 'fps=60',
            '-preset', 'ultrafast',
            '-tune', 'zerolatency',
        ],
        output: screenPath,
    });
    const ffmpegAudio = ffmpegRedux({
        inputArgs: ['-y',
            '-use_wallclock_as_timestamps', '1',
            '-f', 'pulse',
            '-ar', '48000',
            '-ac', '2',
            '-thread_queue_size', '4096',
            '-threads', '0',
        ],
        input: appConfig.ffmpegPulseAudioInput,
        outputArgs: [
            '-c', 'flac',
        ],
        output: path.join(workingDirectoryPath, `audio.${Date.now() - start}.flac`),
    });

    return async () => {
        screenRecordProc.kill('SIGINT');
        ffmpegAudio.stdin.write('q');

        await Promise.all([unwrap(screenRecordProc), unwrap(ffmpegScreen), unwrap(ffmpegAudio)]);
        await fsp.rm(fifoPath);
    };
};
