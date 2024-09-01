import * as fs from 'fs';
import * as path from 'path';

import type { SpawnOptions, Subprocess } from 'bun';

import type { AppConfig } from '.';

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

const ffmpeg = (args: ReadonlyArray<string>) => {
    return spawn(['ffmpeg', ...args], { stdin: 'pipe' });
}

const ffmpegSingleInputSingleOutput = ({ inputArgs, input, outputArgs, output }: { inputArgs: ReadonlyArray<string>, input: string, outputArgs: ReadonlyArray<string>, output: string }) => {
    return ffmpeg([
        ...inputArgs,
        '-i', input,
        ...outputArgs,
        output,
    ]);
}

export const run = async (appConfig: AppConfig, start: number, workingDirectoryPath: string) => {
    const fifoPath = path.join(workingDirectoryPath, 'tmp.fifo');
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

    const screenPath = path.join(workingDirectoryPath, `screenOnly.${Date.now() - start}.mkv`);
    const ffmpegScreen = ffmpegSingleInputSingleOutput({
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
    const ffmpegAudio = [] as { args: ReadonlyArray<string>, abort: AbortController, proc: Subprocess<'pipe', 'pipe', 'inherit'> }[];
    for (const inputKey of appConfig.ffmpegPulseAudioInputs) {
        const ffmpegAudioProc = ffmpegSingleInputSingleOutput({
            inputArgs: ['-y',
                '-use_wallclock_as_timestamps', '1',
                '-f', 'pulse',
                '-ar', '48000',
                '-ac', '2',
                '-thread_queue_size', '4096',
                '-threads', '0',
            ],
            input: inputKey,
            outputArgs: [
                '-c', 'flac',
            ],
            output: path.join(workingDirectoryPath, `audio_${ffmpegAudio.length}.flac`),
        });
        ffmpegAudioProc.abort.signal.addEventListener('abort', () => ffmpegAudioProc.proc.kill('SIGTERM'));
        ffmpegAudio.push(ffmpegAudioProc);
    }

    return async () => {
        screenRecordProc.proc.kill('SIGINT');
        for (const audioProc of ffmpegAudio) {
            audioProc.proc.stdin.write('q');
        }

        await Promise.all([unwrap(screenRecordProc), unwrap(ffmpegScreen), ...ffmpegAudio.map(unwrap)]);
    };
};
