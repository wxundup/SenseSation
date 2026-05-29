using System.Diagnostics;

namespace SenseSation.Capture;

/// <summary>
/// Records the desktop to an mp4 via ffmpeg's gdigrab (Windows). Capturing your
/// own screen for later review is the same as any clip/VOD tool — no game hooks.
/// </summary>
public sealed class ScreenRecorder(CaptureOptions options) : IDisposable
{
    private readonly CaptureOptions _opts = options;
    private Process? _proc;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public string? CurrentFile { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public bool FfmpegAvailable => FfmpegRunner.IsAvailable(_opts.FfmpegPath);

    public string Start()
    {
        if (State != RecordingState.Idle)
            throw new InvalidOperationException($"Recorder is {State}, cannot start.");

        Directory.CreateDirectory(_opts.OutputDirectory);
        string file = Path.Combine(_opts.OutputDirectory, $"vod_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        string audio = string.IsNullOrWhiteSpace(_opts.AudioDevice)
            ? ""
            : $"-f dshow -i audio=\"{_opts.AudioDevice}\" -c:a aac ";

        string args =
            $"-y -f gdigrab -framerate {_opts.Fps} -i desktop {audio}" +
            $"-vf scale={_opts.Width}:{_opts.Height} -c:v libx264 -preset veryfast -pix_fmt yuv420p " +
            $"\"{file}\"";

        _proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _opts.FfmpegPath,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        _proc.Start();
        _ = _proc.StandardError.ReadToEndAsync(); // drain so the buffer never blocks ffmpeg

        State = RecordingState.Recording;
        CurrentFile = file;
        StartedAt = DateTimeOffset.Now;
        return file;
    }

    /// <summary>Gracefully stops ffmpeg (sends 'q') so the mp4 is finalized. Returns the file path.</summary>
    public async Task<string?> StopAsync(CancellationToken ct = default)
    {
        if (State != RecordingState.Recording || _proc is null) return CurrentFile;
        State = RecordingState.Stopping;
        try
        {
            await _proc.StandardInput.WriteLineAsync("q");
            await _proc.StandardInput.FlushAsync(ct);
            if (!_proc.WaitForExit(8000))
                _proc.Kill(entireProcessTree: true);
        }
        catch
        {
            try { _proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        string? file = CurrentFile;
        _proc.Dispose();
        _proc = null;
        State = RecordingState.Idle;
        StartedAt = null;
        return file;
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); } catch { }
        _proc?.Dispose();
    }
}
