/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
using System.Diagnostics;

using Serilog;
using Serilog.Events;

namespace Gracious;

internal sealed class FfmpegProcessWrapper : IDisposable
{
    private static long Incrementer;

    private readonly long _id = Interlocked.Increment(ref Incrementer);

    private readonly SemaphoreSlimWrapper _sem = new(1, 1);

    private readonly TaskCompletionSource _exitCodeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ProcessStartInfo _startInfo;

    private readonly Stream? _outputStream;

    private readonly bool _needsExplicitQuitSignal;

    private Process? _process;

    private Task _outputStreamWriteTask = Task.CompletedTask;

    private bool _ended;

    private FfmpegProcessWrapper(IEnumerable<string> args, Stream? outputStream, bool infiniteInput)
    {
        _startInfo = new()
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _needsExplicitQuitSignal = infiniteInput;
        foreach (string arg in args)
        {
            if (arg == "pipe:0")
            {
                _needsExplicitQuitSignal = false;
            }

            _startInfo.ArgumentList.Add(arg);
        }

        _outputStream = outputStream;
    }

    ~FfmpegProcessWrapper()
    {
        if (!_exitCodeTcs.Task.IsCompleted)
        {
            _process?.TryKill();
        }
    }

    public static FfmpegProcessWrapper? Create(IReadOnlyCollection<string> args, bool infiniteInput = false)
    {
        return args.Count == 0
            ? null
            : new(args, null, infiniteInput);
    }

    public static FfmpegProcessWrapper? Create(IReadOnlyCollection<string> args, Stream outputStream)
    {
        return args.Count == 0
            ? null
            : new(args.Append("pipe:1"), outputStream, infiniteInput: false);
    }

    public async ValueTask WriteToStdinAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        using var _ = await _sem.WaitAsync(cancellationToken);

        if (_process is null)
        {
            throw new InvalidOperationException("Process failed to start.");
        }

        if (_ended)
        {
            throw new InvalidOperationException("Process has already ended.");
        }

        await _process.StandardInput.BaseStream.WriteAsync(data, cancellationToken);
    }

    public void Start()
    {
        using var __ = _sem.Wait();

        if (_process is not null)
        {
            throw new InvalidOperationException("Process has already started.");
        }

        _process = Process.Start(_startInfo);

        if (_process is null)
        {
            throw new InvalidOperationException("Process failed to start.");
        }

        try
        {
            _process.Exited += OnProcessExited;
            _process.EnableRaisingEvents = true;

            if (Log.IsEnabled(LogEventLevel.Debug))
            {
                _process.ErrorDataReceived += OnErrorDataReceived;
            }

            _process.BeginErrorReadLine();

            if (_outputStream is Stream outputStream)
            {
                _outputStreamWriteTask = Task.Run(async () =>
                {
                    await using Stream sourceStream = _process.StandardOutput.BaseStream;
                    await sourceStream.CopyToAsync(outputStream);
                });
            }
            else
            {
                _process.BeginOutputReadLine();
            }
        }
        catch when (_process.TryKill() && false)
        {
            throw;
        }

        void OnErrorDataReceived(object? sender, DataReceivedEventArgs args)
        {
            Log.Debug("{id}: {data}", _id, args.Data);
        }

        void OnProcessExited(object? sender, EventArgs args)
        {
            int? exitCode = (sender as Process)?.ExitCode;
            if (exitCode == 0)
            {
                _exitCodeTcs.TrySetResult();
            }
            else
            {
                _exitCodeTcs.TrySetException(new ProcessExitedWithCodeException(exitCode));
            }
        }
    }

    public async Task End()
    {
        using (_sem.Wait())
        {
            if (_process is null)
            {
                throw new InvalidOperationException("Process has not yet started.");
            }

            if (_ended)
            {
                throw new InvalidOperationException("Process has already ended.");
            }

            if (_needsExplicitQuitSignal)
            {
                try
                {
                    await _process.StandardInput.WriteAsync('q');
                    await _process.StandardInput.FlushAsync();
                }
                catch (IOException ex)
                {
                    Log.Error(ex, "{id}: An unexpected error occurred when trying to end a ffmpeg process gracefully.  Continuing anyway.", _id);
                }
            }

            await _process.StandardInput.BaseStream.DisposeAsync();
            await _outputStreamWriteTask;
            _ended = true;
        }

        await _exitCodeTcs.Task;
        if (_outputStream is not null)
        {
            await _outputStream.FlushAsync();
        }
    }

    public void Dispose()
    {
        using (_process)
        {
            if (!_exitCodeTcs.Task.IsCompleted)
            {
                _process?.TryKill();
            }

            _sem.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
