using System.Text;

namespace Vortex.Desktop.Services;

public sealed class TokenStorageService
{
    private readonly string _path;

    public TokenStorageService()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        var dir = Path.Combine(root, "VortexAI");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "desktop-session.dat");
    }

    public async Task SaveAsync(string token, CancellationToken cancellationToken)
    {
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        await File.WriteAllTextAsync(_path, data, cancellationToken);
        try { File.SetAttributes(_path, File.GetAttributes(_path) | FileAttributes.Hidden); } catch { }
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var data = await File.ReadAllTextAsync(_path, cancellationToken);
            return Encoding.UTF8.GetString(Convert.FromBase64String(data));
        }
        catch { return null; }
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
