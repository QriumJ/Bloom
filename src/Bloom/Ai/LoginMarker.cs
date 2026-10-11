using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Bloom.Ai;

/// .bloom-login in the account's folder holds "<process id> <start time>" while its login terminal is open.
/// The terminal can outlive the wait (Claude Code stays open, the 10 minutes run out) and Bloom itself (a restart),
/// and the CLI may still write into the folder. UsageLogin.Open/Run keep the account from being taken out or signed
/// in again until that process closes; CodexAuth.Store keeps refreshed tokens in a durable pending file meanwhile.
internal static class LoginMarker
{
    private const string Marker = ".bloom-login";

    public static void Mark(string home, Process p)
    {
        try
        {
            File.WriteAllText(Path.Combine(home, Marker), $"{p.Id} {Stamp(p)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            /* no lock across a restart: the wait still holds it */
        }
    }

    public static void Unmark(string home)
    {
        try
        {
            File.Delete(Path.Combine(home, Marker));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* a stale marker is cleared by Alive */
        }
    }

    /// The marked login process is still running (the same process: its start time matches, a reused id doesn't).
    public static bool Alive(string? home)
    {
        if (home is null || !File.Exists(Path.Combine(home, Marker))) return false;
        try
        {
            if (File.ReadAllText(Path.Combine(home, Marker)).Split(' ') is [var id, var stamp] && int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited && Stamp(p) == stamp) return true;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            /* gone, or not readable: stale */
        }
        Unmark(home);
        return false;
    }

    private static string Stamp(Process p) => p.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);
}
