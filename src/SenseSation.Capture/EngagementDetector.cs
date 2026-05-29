using OpenCvSharp;

namespace SenseSation.Capture;

/// <summary>
/// Finds the action in a recording by sampling frames and measuring visual
/// change (motion/flashes/fights produce large frame-to-frame deltas). Pure
/// post-hoc analysis of your own footage — no live processing, no game hooks.
/// </summary>
public sealed class EngagementDetector
{
    private const double SampleHz = 2.0;      // frames analyzed per second
    private const double PeakStdFactor = 1.3; // threshold = mean + factor * stddev
    private const double MergeWindowSec = 6.0; // collapse nearby peaks into one moment
    private const int MaxMoments = 25;

    /// <summary>Returns engagement timestamps plus the clip duration.</summary>
    public Task<(IReadOnlyList<EngagementMoment> moments, TimeSpan duration)> DetectAsync(
        string videoPath, CancellationToken ct = default)
        => Task.Run(() => Detect(videoPath, ct), ct);

    /// <summary>
    /// Grabs a single frame at <paramref name="at"/> as a base64 JPEG data URL,
    /// for an in-memory thumbnail. The video file itself is never retained.
    /// </summary>
    public string? GrabThumbnail(string videoPath, TimeSpan at, int width = 240)
    {
        try
        {
            using var cap = new VideoCapture(videoPath);
            if (!cap.IsOpened()) return null;
            cap.Set(VideoCaptureProperties.PosMsec, Math.Max(0, at.TotalMilliseconds));
            using var frame = new Mat();
            if (!cap.Read(frame) || frame.Empty()) return null;

            int height = frame.Width == 0 ? width : (int)(width * (double)frame.Height / frame.Width);
            using var small = new Mat();
            Cv2.Resize(frame, small, new Size(width, Math.Max(1, height)));
            Cv2.ImEncode(".jpg", small, out var buf, new ImageEncodingParam(ImwriteFlags.JpegQuality, 70));
            return "data:image/jpeg;base64," + Convert.ToBase64String(buf);
        }
        catch { return null; }
    }

    private static (IReadOnlyList<EngagementMoment>, TimeSpan) Detect(string videoPath, CancellationToken ct)
    {
        using var cap = new VideoCapture(videoPath);
        if (!cap.IsOpened()) return ([], TimeSpan.Zero);

        double fps = cap.Fps > 1 ? cap.Fps : 30.0;
        int total = (int)cap.Get(VideoCaptureProperties.FrameCount);
        var duration = total > 0 ? TimeSpan.FromSeconds(total / fps) : TimeSpan.Zero;
        int step = Math.Max(1, (int)Math.Round(fps / SampleHz));

        using var frame = new Mat();
        using var gray = new Mat();
        using var small = new Mat();
        Mat? prev = null;
        var series = new List<(double t, double score)>();

        int idx = 0;
        try
        {
            while (!ct.IsCancellationRequested && cap.Grab())
            {
                if (idx++ % step != 0) continue;
                if (!cap.Retrieve(frame) || frame.Empty()) continue;

                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Resize(gray, small, new Size(320, 180));

                if (prev is not null)
                {
                    using var diff = new Mat();
                    Cv2.Absdiff(small, prev, diff);
                    series.Add(((idx - 1) / fps, Cv2.Mean(diff).Val0));
                    prev.Dispose();
                }
                prev = small.Clone();
            }
        }
        finally { prev?.Dispose(); }

        return (FindPeaks(series), duration);
    }

    private static IReadOnlyList<EngagementMoment> FindPeaks(List<(double t, double score)> series)
    {
        if (series.Count < 3) return [];

        double mean = series.Average(s => s.score);
        double std = Math.Sqrt(series.Average(s => Math.Pow(s.score - mean, 2)));
        double threshold = mean + PeakStdFactor * std;

        // Local maxima above the threshold.
        var candidates = new List<EngagementMoment>();
        for (int i = 1; i < series.Count - 1; i++)
        {
            var (t, s) = series[i];
            if (s >= threshold && s >= series[i - 1].score && s >= series[i + 1].score)
                candidates.Add(new EngagementMoment(TimeSpan.FromSeconds(t), Math.Round(s, 2)));
        }

        // Collapse peaks that are close together, keeping the most intense.
        var merged = new List<EngagementMoment>();
        foreach (var m in candidates.OrderBy(m => m.At))
        {
            if (merged.Count > 0 && (m.At - merged[^1].At).TotalSeconds < MergeWindowSec)
            {
                if (m.Intensity > merged[^1].Intensity) merged[^1] = m;
            }
            else merged.Add(m);
        }

        return merged.OrderByDescending(m => m.Intensity).Take(MaxMoments)
                     .OrderBy(m => m.At).ToList();
    }
}
