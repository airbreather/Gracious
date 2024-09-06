import * as path from 'path';

import type { Subprocess } from 'bun';

import type { AppConfig } from '.';

export const procs = [] as { args: ReadonlyArray<string>, proc: Subprocess, abort: AbortController }[];

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
    const screenRecordProc = Bun.spawn([appConfig.recordScreenExe, workingDirectoryPath, `${Date.now() - start}`], { env: { ...process.env, 'RUST_BACKTRACE': 'full' } });
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
    };
};
