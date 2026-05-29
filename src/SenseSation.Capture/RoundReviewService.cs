using System.Diagnostics;

namespace SenseSation.Capture;

/// <summary>
/// Live "record → analyze → delete → repeat" reviewer. ffmpeg captures the screen
/// in short segments; each finished segment is analyzed for action, in-memory
/// thumbnails of the moments are kept, then the segment file is DELETED. Nothing
/// is persisted to disk. Pure post-segment review of your own footage — no live
/// game hooks, no in-round feedback.
/// </summary>
public sealed class RoundReviewService(CaptureOptions options, EngagementDetector detector) : IDisposable
{
    private readonly CaptureOptions _opts = options;
    private readonly EngagementDetector _detector = detector;

    private Process? _ffmpeg;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string? _segDir;
    private readonly List<RoundReview> _reviews = [];
    private readonly HashSet<string> _processed = [];

    /// <summary>Segment length in seconds (≈ a round). Footage is deleted right after analysis.</summary>
    public int SegmentSeconds { get; set; } = 90;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public bool FfmpegAvailable => FfmpegRunner.IsAvailable(_opts.FfmpegPath);
    public IReadOnlyList<RoundReview> Reviews => _reviews;
    public string? Error { get; private set; }

    /// <summary>Raised whenever a new segment review is added or state changes.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (State != RecordingState.Idle) return;
        Error = null;
        _reviews.Clear();
        _processed.Clear();

        _segDir = Path.Combine(_opts.OutputDirectory, "live_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(_segDir);
        string pattern = Path.Combine(_segDir, "seg_%05d.mp4");

        string args =
            $"-y -f gdigrab -framerate {_opts.Fps} -i desktop " +
            $"-vf scale={_opts.Width}:{_opts.Height} -c:v libx264 -preset veryfast -pix_fmt yuv420p " +
            $"-f segment -segment_time {SegmentSeconds} -reset_timestamps 1 -segment_format mp4 \"{pattern}\"";

        _ffmpeg = new Process
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
        _ffmpeg.Start();
        _ = _ffmpeg.StandardError.ReadToEndAsync();

        State = RecordingState.Recording;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        Raise();
    }

    public async Task StopAsync()
    {
        if (State != RecordingState.Recording) return;
        State = RecordingState.Stopping;
        Raise();

        try
        {
            await _ffmpeg!.StandardInput.WriteLineAsync("q");
            if (!_ffmpeg.WaitForExit(8000)) _ffmpeg.Kill(entireProcessTree: true);
        }
        catch { try { _ffmpeg?.Kill(entireProcessTree: true); } catch { } }

        _cts?.Cancel();
        try { if (_loop is not null) await _loop; } catch { }

        // Analyze any remaining segments, including the final finalized one.
        await ProcessReadyAsync(force: true, CancellationToken.None);

        try { if (_segDir is not null) Directory.Delete(_segDir, recursive: true); } catch { }

        _ffmpeg?.Dispose();
        _ffmpeg = null;
        State = RecordingState.Idle;
        Raise();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await ProcessReadyAsync(force: false, ct); } catch { }
            try { await Task.Delay(2000, ct); } catch { }
        }
    }

    private async Task ProcessReadyAsync(bool force, CancellationToken ct)
    {
        if (_segDir is null || !Directory.Exists(_segDir)) return;
        var files = Directory.GetFiles(_segDir, "seg_*.mp4").OrderBy(f => f).ToList();
        if (files.Count == 0) return;

        // Every segment except the one ffmpeg is still writing is complete.
        int complete = force ? files.Count : files.Count - 1;
        for (int i = 0; i < complete && !ct.IsCancellationRequested; i++)
        {
            if (_processed.Add(files[i]))
                await AnalyzeSegmentAsync(files[i], ct);
        }
    }

    private async Task AnalyzeSegmentAsync(string file, CancellationToken ct)
    {
        try
        {
            var (moments, length) = await _detector.DetectAsync(file, ct);
            var shots = new List<MomentShot>();
            foreach (var m in moments.Take(6))
                shots.Add(new MomentShot(m.At, m.Intensity, _detector.GrabThumbnail(file, m.At)));

            _reviews.Add(new RoundReview
            {
                Index = _reviews.Count + 1,
                At = DateTimeOffset.Now,
                Length = length,
                Moments = shots,
            });
        }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            try { File.Delete(file); } catch { } // footage discarded — nothing persisted
            Raise();
        }
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(entireProcessTree: true); } catch { }
        _ffmpeg?.Dispose();
        _cts?.Dispose();
    }
}
