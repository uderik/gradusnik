using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace Gradusnik;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "Gradusnik_SingleInstance", out bool first);
        if (!first) return;

        Application.ThreadException += (_, e) => Log.Write($"Необработанная ошибка: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Необработанная ошибка: {e.ExceptionObject}");
        Log.Write("Запуск");

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}

sealed class TrayApp : ApplicationContext
{
    const string TaskName = "Gradusnik";

    readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true };
    readonly NotifyIcon _cpuIcon = new() { Visible = true };
    readonly NotifyIcon _gpuIcon = new() { Visible = true };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    readonly ToolStripMenuItem _autostartItem;
    readonly ToolStripMenuItem _alertsItem;
    readonly Settings _settings = Settings.Load();
    readonly Alert _cpuAlert = new("CPU");
    readonly Alert _gpuAlert = new("GPU");
    readonly Alert _gpuHotAlert = new("GPU Hot Spot");

    // Если датчики не отвечают дольше этого времени, иконки становятся серыми
    static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);
    static readonly TimeSpan SlowUpdate = TimeSpan.FromSeconds(3);

    readonly Thread _pollThread;
    volatile bool _stopping;
    volatile Sample? _latest;
    Sample? _shown;

    public TrayApp()
    {
        _computer.Open();

        _autostartItem = new ToolStripMenuItem("Запускать при входе в Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = IsAutostartEnabled()
        };
        _alertsItem = new ToolStripMenuItem("Оповещать о перегреве", null, (_, _) => ToggleAlerts())
        {
            Checked = _settings.AlertsEnabled
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_alertsItem);
        menu.Items.Add("Пороги оповещений…", null, (_, _) => Settings.OpenInEditor());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        _cpuIcon.ContextMenuStrip = menu;
        _gpuIcon.ContextMenuStrip = menu;

        // Опрос датчиков идёт в фоновом потоке: если какой-то датчик завис, трей продолжает отвечать
        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "SensorPoll" };
        _pollThread.Start();

        _timer.Tick += (_, _) => RefreshTray();
        _timer.Start();
    }

    void PollLoop()
    {
        while (!_stopping)
        {
            try
            {
                _latest = ReadSensors();
            }
            catch (Exception e)
            {
                Log.Write($"Ошибка опроса датчиков: {e}");
            }
            Thread.Sleep(2000);
        }
    }

    Sample ReadSensors()
    {
        foreach (var hw in _computer.Hardware)
        {
            var sw = Stopwatch.StartNew();
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();
            if (sw.Elapsed > SlowUpdate)
                Log.Write($"Медленный опрос: {hw.HardwareType} \"{hw.Name}\" — {sw.Elapsed.TotalSeconds:0.0} с");
        }

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // Дискретная видеокарта важнее встроенной, поэтому NVIDIA идёт первой
        var gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
               ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
               ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);

        float? cpuTemp = Find(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core Average")
                         ?? Max(cpu, SensorType.Temperature);
        float? cpuLoad = Find(cpu, SensorType.Load, "CPU Total");
        float? cpuPower = Find(cpu, SensorType.Power, "Package", "CPU Package");

        float? gpuTemp = Find(gpu, SensorType.Temperature, "GPU Core");
        float? gpuHot = Find(gpu, SensorType.Temperature, "GPU Hot Spot");
        float? gpuLoad = Find(gpu, SensorType.Load, "GPU Core");
        float? gpuPower = Find(gpu, SensorType.Power, "GPU Package", "GPU Power");

        var ssdTemps = _computer.Hardware
            .Where(h => h.HardwareType == HardwareType.Storage)
            .Select(h => Max(h, SensorType.Temperature))
            .Where(t => t.HasValue)
            .Select(t => $"{t:0}°")
            .ToList();

        var ssd = ssdTemps.Count > 0 ? $"\nSSD: {string.Join(" ", ssdTemps)}" : "";
        return new Sample(
            DateTime.UtcNow,
            cpuTemp,
            Trim($"CPU {cpuTemp:0}°C | {cpuLoad:0}% | {cpuPower:0} W\n{cpu?.Name}"),
            gpuTemp,
            gpuHot,
            Trim($"GPU {gpuTemp:0}°C (hot spot {gpuHot:0}°) | {gpuLoad:0}% | {gpuPower:0} W{ssd}"));
    }

    // Вызывается в потоке интерфейса: только рисует последний готовый замер, сама к датчикам не обращается
    void RefreshTray()
    {
        _settings.ReloadIfChanged();
        var s = _latest;

        if (s == null || DateTime.UtcNow - s.Time > StaleAfter)
        {
            if (_shown != null || s == null)
            {
                SetIcon(_cpuIcon, null, _settings.CpuAlert, Color.Gray);
                SetIcon(_gpuIcon, null, _settings.GpuAlert, Color.Gray);
                _cpuIcon.Text = _gpuIcon.Text = s == null ? "Gradusnik: читаю датчики…" : "Gradusnik: датчики не отвечают";
                if (s != null) Log.Write("Датчики не отвечают дольше 15 с");
                _shown = null;
            }
            return;
        }
        if (ReferenceEquals(s, _shown)) return;
        _shown = s;

        SetIcon(_cpuIcon, s.CpuTemp, _settings.CpuAlert, Color.FromArgb(90, 170, 255));
        _cpuIcon.Text = s.CpuText;
        SetIcon(_gpuIcon, s.GpuTemp, _settings.GpuAlert, Color.FromArgb(120, 220, 120));
        _gpuIcon.Text = s.GpuText;

        if (_settings.AlertsEnabled)
        {
            _cpuAlert.Check(s.CpuTemp, _settings.CpuAlert, _cpuIcon);
            _gpuAlert.Check(s.GpuTemp, _settings.GpuAlert, _gpuIcon);
            _gpuHotAlert.Check(s.GpuHot, _settings.GpuHotSpotAlert, _gpuIcon);
        }
    }

    sealed record Sample(DateTime Time, float? CpuTemp, string CpuText, float? GpuTemp, float? GpuHot, string GpuText);

    void ToggleAlerts()
    {
        _settings.AlertsEnabled = !_settings.AlertsEnabled;
        _settings.Save();
        _alertsItem.Checked = _settings.AlertsEnabled;
    }

    static float? Find(IHardware? hw, SensorType type, params string[] names)
    {
        if (hw == null) return null;
        var sensors = AllSensors(hw).Where(s => s.SensorType == type && s.Value.HasValue).ToList();
        foreach (var name in names)
        {
            var s = sensors.FirstOrDefault(x => x.Name == name);
            if (s != null) return s.Value;
        }
        return null;
    }

    static float? Max(IHardware? hw, SensorType type) =>
        hw == null ? null : AllSensors(hw).Where(s => s.SensorType == type && s.Value > 0).Max(s => s.Value);

    static IEnumerable<ISensor> AllSensors(IHardware hw) =>
        hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors));

    // Всплывающая подсказка трея ограничена 127 символами
    static string Trim(string s) => s.Length > 127 ? s[..127] : s;

    static void SetIcon(NotifyIcon icon, float? temp, float alertAt, Color labelColor)
    {
        int size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            string text = temp.HasValue ? Math.Round(temp.Value).ToString("0") : "--";
            // Красный — порог оповещения достигнут, оранжевый — осталось меньше 15°C
            var color = temp switch
            {
                null => Color.Gray,
                var t when t >= alertAt => Color.FromArgb(255, 70, 70),
                var t when t >= alertAt - 15 => Color.FromArgb(255, 170, 40),
                _ => labelColor
            };

            // Подбираем самый крупный шрифт, при котором текст влезает в иконку
            float fontSize = size;
            SizeF measured;
            Font font;
            do
            {
                font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                measured = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
                if (measured.Width <= size && measured.Height <= size * 1.05f) break;
                font.Dispose();
                fontSize -= 0.5f;
            } while (fontSize > 6);

            using (font)
            using (var brush = new SolidBrush(color))
            {
                var x = (size - measured.Width) / 2;
                var y = (size - measured.Height) / 2;
                g.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        var old = icon.Icon;
        icon.Icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        old?.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    // Автозапуск через Планировщик заданий: только так программа с правами администратора стартует без запроса UAC
    static bool IsAutostartEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    void ToggleAutostart()
    {
        if (IsAutostartEnabled())
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        else
            RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{Environment.ProcessPath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
        _autostartItem.Checked = IsAutostartEnabled();
    }

    static int RunSchtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        p.WaitForExit();
        return p.ExitCode;
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _stopping = true;
        _cpuIcon.Visible = false;
        _gpuIcon.Visible = false;
        _cpuIcon.Dispose();
        _gpuIcon.Dispose();
        // Закрываем драйвер, только если опрос завершился; зависший поток фоновый и умрёт вместе с процессом
        if (_pollThread.Join(TimeSpan.FromSeconds(3)))
            _computer.Close();
        base.ExitThreadCore();
    }
}
