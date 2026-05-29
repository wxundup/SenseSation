namespace SenseSation.Capture;

public enum RecordingState { Idle, Recording, Stopping }

/// <summary>A point of detected on-screen activity within a recording.</summary>
public sealed record EngagementMoment(TimeSpan At, double Intensity);

/// <summary>An exported review clip centered on an engagement.</summary>
public sealed record ReviewClip(int Index, string FilePath, TimeSpan At, TimeSpan Start, TimeSpan Duration)
{
    public string Label => $"#{Index} @ {At:mm\\:ss}";
}

/// <summary>The result of analyzing a recording: the source plus its review clips.</summary>
public sealed record ReviewSession
{
    public required string VideoPath { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<EngagementMoment> Moments { get; init; } = [];
    public IReadOnlyList<ReviewClip> Clips { get; init; } = [];
}

/// <summary>A detected moment with an in-memory thumbnail (data URL) — footage itself is discarded.</summary>
public sealed record MomentShot(TimeSpan At, double Intensity, string? ThumbnailDataUrl);

/// <summary>
/// Review of one recorded segment: when it happened, how long, and the action
/// moments with thumbnails. Built after the segment is analyzed; the source video
/// is then deleted, so nothing is persisted to disk.
/// </summary>
public sealed record RoundReview
{
    public required int Index { get; init; }
    public DateTimeOffset At { get; init; }
    public TimeSpan Length { get; init; }
    public IReadOnlyList<MomentShot> Moments { get; init; } = [];
    public int FightCount => Moments.Count;
}
