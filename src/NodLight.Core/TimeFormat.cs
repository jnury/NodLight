namespace NodLight.Core;

public static class TimeFormat
{
    /// <summary>Compact elapsed time: "12 s", "4 min", "1 h". Floors; negative values count as 0 s.</summary>
    public static string Short(TimeSpan t)
    {
        var seconds = (long)Math.Floor(Math.Max(0, t.TotalSeconds));
        if (seconds < 60) return $"{seconds} s";
        if (seconds < 3600) return $"{seconds / 60} min";
        return $"{seconds / 3600} h";
    }
}
