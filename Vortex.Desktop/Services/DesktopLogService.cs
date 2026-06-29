using System.Text;

namespace Vortex.Desktop.Services;

public static class DesktopLogService
{
    private static readonly object Gate = new();
    private static readonly string LogPath = CreateLogPath();

    public static void Info(string message) => Write("INFO", message, null);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = $"{DateTimeOffset.UtcNow:O} [{level}] {message}";
            if (exception is not null)
            {
                line += $" | {exception.GetType().Name}: {exception.Message}";
            }

            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the desktop auth flow.
        }
    }

    private static string CreateLogPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        var dir = Path.Combine(root, "VortexAI", "logs");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"desktop-{DateTimeOffset.UtcNow:yyyyMMdd}.log");
    }
}
