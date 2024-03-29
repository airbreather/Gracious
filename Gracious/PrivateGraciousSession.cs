﻿/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
namespace Gracious;

internal sealed class PrivateGraciousSession
{
    private FfmpegProcessWrapper? _process;

    private int _state;

    public PrivateGraciousSession(DesktopRecordingFfmpegArgs args, string emergencyFolderPath)
    {
        try
        {
            string videoOnlyPath = Path.Combine(emergencyFolderPath, "Screen.mkv");
            string audioOnlyPath = Path.Combine(emergencyFolderPath, "Audio.flac");
            string combinedPath = Path.Combine(emergencyFolderPath, "Desktop.mkv");

            List<string> combinedArgs =
            [
                ..args.DesktopScreen.InputFlags,
                "-i", args.DesktopScreen.Input,
                ..args.DesktopAudio.InputFlags,
                "-i", args.DesktopAudio.Input,
                ..args.DesktopScreen.RealtimeOutputFlags,
                ..args.DesktopAudio.RealtimeOutputFlags.Select(f => f.Replace("{streamIndex}", "0")),
                "-map", "0",
                "-map", "1",
            ];

            // https://trac.ffmpeg.org/ticket/10131
            const bool FFMPEG_10131_IS_FIXED = false;
            if (FFMPEG_10131_IS_FIXED)
            {
                combinedArgs.AddRange(
                [
                    "-f", "tee",
                    "-use_fifo", "1",
                    "-fifo_options", "queue_size=200:drop_pkts_on_overflow=1:attempt_recovery=1:recover_any_error=1:recovery_wait_time=100ms",
                    $"[select=v]{videoOnlyPath}|[select=a]{audioOnlyPath}|[select=v,a]{combinedPath}",
                ]);
            }
            else
            {
                combinedArgs.Add(combinedPath);
            }

            _process = FfmpegProcessWrapper.Create(combinedArgs, infiniteInput: true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Unhandled exception in c'tor.");
            throw;
        }
    }

    public void Start()
    {
        switch (Interlocked.CompareExchange(ref _state, 1, 0))
        {
            case 0:
                break;

            case 1:
                throw new InvalidOperationException("Already started.");

            case 2:
                throw new NotSupportedException("Cannot start again after stopping.");

            default:
                throw new InvalidOperationException("Unknown state.");
        }

        _process?.Start();
    }

    public void Stop()
    {
        switch (Interlocked.CompareExchange(ref _state, 2, 1))
        {
            case 0:
                throw new InvalidOperationException("Cannot stop before starting.");

            case 1:
                break;

            case 2:
                throw new InvalidOperationException("Already stopped.");

            default:
                throw new InvalidOperationException("Unknown state.");
        }

        if (_process?.End() is not Task task)
        {
            task = Task.CompletedTask;
        }

        _process = null;
        task.GetAwaiter().GetResult();
    }
}
