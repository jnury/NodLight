using System.Diagnostics;
using System.IO;

namespace NodLight.App;

/// <summary>
/// Appends trace output to a log file so Guard/Trace messages are visible outside a debugger. When the file grows past
/// <c>maxBytes</c> it is truncated and starts over. Never throws: losing a log line beats taking the deck down.
/// </summary>
public sealed class FileTraceListener(string path, long maxBytes) : TraceListener
{
    readonly Lock gate = new();

    public override void Write(string? message) => Append(message);

    public override void WriteLine(string? message) =>
        Append($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");

    void Append(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                var mode = info.Exists && info.Length > maxBytes ? FileMode.Create : FileMode.Append;
                using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.Write(text);
            }
            catch (Exception)
            {
                // ignored: see the class summary
            }
        }
    }
}
