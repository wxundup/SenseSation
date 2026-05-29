namespace SenseSation.Capture;

/// <summary>Configuration for screen recording and VOD analysis.</summary>
public sealed class CaptureOptions
{
    public const string Section = "Capture";

    /// <summary>Path to the ffmpeg executable. "ffmpeg" resolves it from PATH.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Where recordings and exported clips are written.</summary>
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "SenseSation");

    public int Fps { get; set; } = 60;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;

    /// <summary>
    /// Optional DirectShow audio input device name (e.g. "Stereo Mix" or a
    /// loopback capture device). Empty = video only. VOD analysis works either
    /// way: it uses visual motion detection, not audio.
    /// </summary>
    public string AudioDevice { get; set; } = "";

    /// <summary>Seconds of footage to keep before/after each detected engagement.</summary>
    public double ClipPreSeconds { get; set; } = 4;
    public double ClipPostSeconds { get; set; } = 3;
}
