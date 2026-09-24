namespace Gradusnik;

// Оповещает, когда температура несколько замеров подряд держится выше порога.
// Повторное оповещение возможно только после остывания на Hysteresis градусов ниже порога.
sealed class Alert
{
    const int ReadingsToFire = 3;   // 3 замера по 2 секунды, чтобы короткие скачки не вызывали оповещение
    const float Hysteresis = 5;

    readonly string _name;
    int _overCount;
    bool _fired;

    public Alert(string name) => _name = name;

    public void Check(float? temp, float threshold, NotifyIcon icon)
    {
        if (temp is not float t) return;

        if (t >= threshold)
        {
            if (++_overCount >= ReadingsToFire && !_fired)
            {
                _fired = true;
                icon.ShowBalloonTip(10000, $"Перегрев: {_name} {t:0}°C",
                    $"Температура {_name} выше порога {threshold:0}°C.", ToolTipIcon.Warning);
            }
        }
        else
        {
            _overCount = 0;
            if (t < threshold - Hysteresis) _fired = false;
        }
    }
}
