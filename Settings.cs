using System.Diagnostics;
using System.Text.Json;

namespace Gradusnik;

// Настройки хранятся в %APPDATA%\Gradusnik\settings.json и перечитываются при изменении файла
sealed class Settings
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gradusnik");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public bool AlertsEnabled { get; set; } = true;
    public float CpuAlert { get; set; } = 85;
    public float GpuAlert { get; set; } = 83;
    public float GpuHotSpotAlert { get; set; } = 100;

    DateTime _loadedWriteTime;

    public static Settings Load()
    {
        var s = new Settings();
        if (!File.Exists(FilePath)) s.Save();
        s.ReloadIfChanged();
        return s;
    }

    public void ReloadIfChanged()
    {
        try
        {
            var writeTime = File.GetLastWriteTimeUtc(FilePath);
            if (writeTime == _loadedWriteTime) return;
            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
            if (loaded != null)
            {
                AlertsEnabled = loaded.AlertsEnabled;
                CpuAlert = loaded.CpuAlert;
                GpuAlert = loaded.GpuAlert;
                GpuHotSpotAlert = loaded.GpuHotSpotAlert;
            }
            _loadedWriteTime = writeTime;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // Файл пишется в редакторе или в нём опечатка: оставляем прежние значения
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
    }

    public static void OpenInEditor() =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{FilePath}\"") { UseShellExecute = true });
}
