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

    public TrayApp()
    {
        _computer.Open();

        _autostartItem = new ToolStripMenuItem("Запускать при входе в Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = IsAutostartEnabled()
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        _cpuIcon.ContextMenuStrip = menu;
        _gpuIcon.ContextMenuStrip = menu;

        _timer.Tick += (_, _) => UpdateSensors();
        _timer.Start();
        UpdateSensors();
    }

    void UpdateSensors()
    {
        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();
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

        SetIcon(_cpuIcon, cpuTemp, Color.FromArgb(90, 170, 255));
        _cpuIcon.Text = Trim($"CPU {cpuTemp:0}°C | {cpuLoad:0}% | {cpuPower:0} W\n{cpu?.Name}");

        SetIcon(_gpuIcon, gpuTemp, Color.FromArgb(120, 220, 120));
        var ssd = ssdTemps.Count > 0 ? $"\nSSD: {string.Join(" ", ssdTemps)}" : "";
        _gpuIcon.Text = Trim($"GPU {gpuTemp:0}°C (hot spot {gpuHot:0}°) | {gpuLoad:0}% | {gpuPower:0} W{ssd}");
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

    static void SetIcon(NotifyIcon icon, float? temp, Color labelColor)
    {
        int size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            string text = temp.HasValue ? Math.Round(temp.Value).ToString("0") : "--";
            var color = temp switch
            {
                null => Color.Gray,
                >= 85 => Color.FromArgb(255, 70, 70),
                >= 70 => Color.FromArgb(255, 170, 40),
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
        _cpuIcon.Visible = false;
        _gpuIcon.Visible = false;
        _cpuIcon.Dispose();
        _gpuIcon.Dispose();
        _computer.Close();
        base.ExitThreadCore();
    }
}
