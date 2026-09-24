namespace Gradusnik;

// Журнал в %APPDATA%\Gradusnik\log.txt; при превышении 1 МБ старый журнал переименовывается в log.old.txt
static class Log
{
    const long MaxSize = 1024 * 1024;

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gradusnik");
    static readonly string FilePath = Path.Combine(Dir, "log.txt");
    static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Dir);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxSize)
                    File.Move(FilePath, Path.Combine(Dir, "log.old.txt"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Журнал не должен ронять программу
        }
    }
}
