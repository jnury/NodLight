namespace NodLight.Core;

public static class ExecutableLocator
{
    /// <summary>
    /// The first existing <paramref name="fileName"/> in the PATH directories (in order), then in
    /// <paramref name="fallbackDirs"/>; null when it is nowhere. Blank and malformed PATH entries are skipped.
    /// </summary>
    public static string? Find(string fileName, string? path, IEnumerable<string> fallbackDirs, Func<string, bool> exists) =>
        (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => dir.Trim('"'))
            .Concat(fallbackDirs)
            .Where(dir => dir.Length > 0)
            .Select(dir => TryCombine(dir, fileName))
            .FirstOrDefault(candidate => candidate is not null && exists(candidate));

    static string? TryCombine(string dir, string fileName)
    {
        try
        {
            return Path.Combine(dir, fileName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
