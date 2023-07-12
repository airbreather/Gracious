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

            List<string> combinedArgs = new();
            combinedArgs.AddRange(args.DesktopScreen.InputFlags);
            combinedArgs.Add("-i");
            combinedArgs.Add(args.DesktopScreen.Input);

            combinedArgs.AddRange(args.DesktopAudio.InputFlags);
            combinedArgs.Add("-i");
            combinedArgs.Add(args.DesktopAudio.Input);

            combinedArgs.AddRange(args.DesktopScreen.OutputFlags);
            combinedArgs.AddRange(args.DesktopAudio.OutputFlags);

            combinedArgs.Add("-map");
            combinedArgs.Add("0");
            combinedArgs.Add("-map");
            combinedArgs.Add("1");

            // https://trac.ffmpeg.org/ticket/10131
            const bool FFMPEG_10131_IS_FIXED = false;
            if (FFMPEG_10131_IS_FIXED)
            {
                combinedArgs.Add("-f");
                combinedArgs.Add("tee");
                combinedArgs.Add("-use_fifo");
                combinedArgs.Add("1");
                combinedArgs.Add("-fifo_options");
                combinedArgs.Add("queue_size=200:drop_pkts_on_overflow=1:attempt_recovery=1:recover_any_error=1:recovery_wait_time=100ms");

                combinedArgs.Add($"[select=v]{videoOnlyPath}|[select=a]{audioOnlyPath}|[select=v,a]{combinedPath}");
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
