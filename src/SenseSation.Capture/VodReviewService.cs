using System.Globalization;

namespace SenseSation.Capture;

/// <summary>
/// Orchestrates VOD review: detect engagements in a recording, then export a
/// short clip around each so you can review your fights without scrubbing.
/// </summary>
public sealed class VodReviewService(CaptureOptions options, EngagementDetector detector)
{
    private readonly CaptureOptions _opts = options;
    private readonly EngagementDetector _detector = detector;

    public async Task<ReviewSession> AnalyzeAsync(string videoPath, bool exportClips = true, CancellationToken ct = default)
    {
        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Recording not found.", videoPath);

        var (moments, duration) = await _detector.DetectAsync(videoPath, ct);

        var clips = exportClips
            ? await ExportClipsAsync(videoPath, moments, ct)
            : [];

        return new ReviewSession
        {
            VideoPath = videoPath,
            Duration = duration,
            Moments = moments,
            Clips = clips,
        };
    }

    private async Task<IReadOnlyList<ReviewClip>> ExportClipsAsync(
        string videoPath, IReadOnlyList<EngagementMoment> moments, CancellationToken ct)
    {
        if (moments.Count == 0) return [];

        string clipDir = Path.Combine(
            Path.GetDirectoryName(videoPath) ?? _opts.OutputDirectory,
            Path.GetFileNameWithoutExtension(videoPath) + "_clips");
        Directory.CreateDirectory(clipDir);

        var clips = new List<ReviewClip>();
        int i = 1;
        foreach (var m in moments)
        {
            ct.ThrowIfCancellationRequested();
            double start = Math.Max(0, m.At.TotalSeconds - _opts.ClipPreSeconds);
            double dur = _opts.ClipPreSeconds + _opts.ClipPostSeconds;
            string outFile = Path.Combine(clipDir, $"clip_{i:00}_{(int)m.At.TotalSeconds:00000}s.mp4");

            // Re-encode (not stream-copy) so the cut starts on a clean keyframe.
            string args = string.Format(CultureInfo.InvariantCulture,
                "-y -ss {0:0.###} -i \"{1}\" -t {2:0.###} -c:v libx264 -preset veryfast -pix_fmt yuv420p \"{3}\"",
                start, videoPath, dur, outFile);

            var (exit, _) = await FfmpegRunner.RunAsync(_opts.FfmpegPath, args, ct);
            if (exit == 0 && File.Exists(outFile))
                clips.Add(new ReviewClip(i, outFile, m.At,
                    TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(dur)));
            i++;
        }
        return clips;
    }
}
