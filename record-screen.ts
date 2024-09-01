import * as fs from 'fs';
import * as path from 'path';
import * as util from 'util';

import type { Subprocess } from 'bun';

import ffmpeg, { FfmpegCommand } from 'fluent-ffmpeg';

import type { AppConfig } from '.';
import escapeStringRegexp from 'escape-string-regexp';

interface format {
    description: string,
    canDemux: boolean,
    canMux: boolean,
};

type formats = Record<string, format>;

type formatCallback = (err: Error | null, formats?: formats) => { };

const oldGetAvailableFormats = ffmpeg.prototype.getAvailableFormats;
ffmpeg.prototype.availableFormats =
ffmpeg.prototype.getAvailableFormats = function(callback: formatCallback) {
    const callback2 = function(err: Error | null, formats?: formats) {
        if (!formats) {
            callback(err);
        } else {
            callback(null, {...formats, pulse: { description: 'Pulse audio output', canDemux: true, canMux: true } });
        }
    };
    return oldGetAvailableFormats.apply(this, [callback2, ...[...arguments].slice(1)]);
}

const unwrap = async <T extends Subprocess>(subprocess: T) => {
    const exitCode = await subprocess.exited;
    if (exitCode !== 0 && exitCode !== 130) {
        throw new Error(`Process exited with code ${exitCode}`);
    }
}

const createPromises = (ffmpeg: FfmpegCommand) => {
    let resolveCmdline: ((value: string | PromiseLike<string>) => void) = () => { };
    let rejectCmdline: ((reason?: any) => void) | null = () => { };
    const cmdline = new Promise<string>((resolve, reject) => {
        resolveCmdline = value => {
            rejectCmdline = null;
            resolve(value);
        };
        rejectCmdline = err => {
            rejectCmdline = null;
            reject(err);
        };
    });

    const proc = new Promise<void>((resolve, reject) => {
        const onError = (err: Error) => {
            if (rejectCmdline) {
                const rejectCmdline_ = rejectCmdline;
                rejectCmdline = null;
                rejectCmdline_(util.inspect({ reason: 'error before start', err }, undefined, 99, true));
            }

            reject(err);
        };

        const onEnd = () => {
            if (rejectCmdline) {
                const rejectCmdline_ = rejectCmdline;
                rejectCmdline = null;
                rejectCmdline_(util.inspect({ reason: 'never called start' }, undefined, 99, true));
            }

            resolve();
        };

        ffmpeg
            .on('start', resolveCmdline)
            .on('error', err => {
                if (err.message.match(escapeStringRegexp('255: Exiting normally'))) {
                    onEnd();
                } else {
                    onError(err);
                }
            })
            .on('end', onEnd);
    });

    return { cmdline, proc };
}

const createFfmpegScreen = (inputPath: string, outputPath: string) =>
    ffmpeg(inputPath)
        .inputOption('-use_wallclock_as_timestamps', '1')
        .inputOption('-f', 'rawvideo')
        .inputOption('-pix_fmt', 'bgra')
        .inputOption('-s', '2560x1368')
        .output(outputPath)
        .videoCodec('libx264rgb')
        .videoFilter([
            { filter: 'fps', options: '60' },
        ])
        .outputOption('-crf', '0')
        .outputOption('-preset', 'ultrafast')
        .outputOption('-tune', 'zerolatency');

const createFfmpegAudio = (input: string, outputPath: string) =>
    ffmpeg(input)
        .inputOption('-f', 'pulse')
        .inputOption('-use_wallclock_as_timestamps', '1')
        .inputOption('-ar', '48000')
        .inputOption('-ac', '2')
        .inputOption('-thread_queue_size', '4096')
        .inputOption('-threads', '0')
        .output(outputPath)
        .audioCodec('flac');

export const run = async (appConfig: AppConfig, start: number, workingDirectoryPath: string) => {
    const fifoPath = path.join(workingDirectoryPath, 'tmp.fifo');
    const recordScreen = Bun.spawn([appConfig.recordScreenExe, fifoPath], { env: { ...process.env, 'RUST_BACKTRACE': 'full' } });

    await new Promise<void>(resolve => {
        const interval = setInterval(() => {
            if (fs.existsSync(fifoPath)) {
                clearInterval(interval);
                resolve();
            }
        }, 100);
    })

    const screenPath = path.join(workingDirectoryPath, `screenOnly.${Date.now() - start}.mkv`);
    const ffmpegScreen = createFfmpegScreen(fifoPath, screenPath);
    console.log('maybe screen:', ffmpegScreen._getArguments());
    const ffmpegScreenPromises = createPromises(ffmpegScreen);
    const ffmpegAudio = createFfmpegAudio(appConfig.ffmpegPulseAudioInput, path.join(workingDirectoryPath, `desktopAudio.${Date.now() - start}.flac`));
    console.log('maybe audio:', ffmpegAudio._getArguments());
    const ffmpegAudioPromises = createPromises(ffmpegAudio);

    let ffmpegScreenCmdline: string | null = null;
    ffmpegScreenPromises.cmdline.then(cmdline => console.log('started recording ffmpeg screen, cmdline:', ffmpegScreenCmdline = cmdline));
    ffmpegScreen.run();

    let ffmpegAudioCmdline: string | null = null;
    ffmpegAudioPromises.cmdline.then(cmdline => console.log('started recording desktop audio, cmdline:', ffmpegAudioCmdline = cmdline));
    ffmpegAudio.run();

    return async () => {
        ffmpegScreen.kill('SIGINT');
        ffmpegAudio.kill('SIGINT');
        recordScreen.kill('SIGINT');
        await Promise.all([unwrap(recordScreen), ffmpegScreenPromises.proc, ffmpegAudioPromises.proc]);
    };
};
