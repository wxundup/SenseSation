using System.Diagnostics;

namespace SenseSation.Capture;

/// <summary>Thin wrapper for invoking ffmpeg as a child process.</summary>
internal static class FfmpegRunner
{
    /// <summary>Runs ffmpeg to completion, returning (exitCode, stderr). ffmpeg logs to stderr.</summary>
    public static async Task<(int exitCode, string stderr)> RunAsync(
        string ffmpegPath, string arguments, CancellationToken ct = default)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        proc.Start();
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, await stderrTask);
    }

    public static bool IsAvailable(string ffmpegPath)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return false;
            proc.WaitForExit(4000);
            return proc.HasExited && proc.ExitCode == 0;
        }
        catch { return false; }
    }
}
