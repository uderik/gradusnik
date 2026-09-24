# Градусник (Gradusnik)

Маленькая утилита для Windows 11: показывает температуру CPU и GPU цифрами прямо в трее.

- Две иконки: **синяя** — CPU, **зелёная** — GPU. За 15°C до порога оповещения цифра становится оранжевой, на пороге — красной.
- Подсказка при наведении: загрузка, потребление в ваттах, hot spot видеокарты, температура SSD.
- Уведомление Windows о перегреве, если температура держится выше порога 6 секунд. Повторно оповещает только после остывания на 5°C.
- Правый клик: включить или выключить оповещения, изменить пороги, автозапуск при входе в Windows (через Планировщик заданий, без запроса UAC), выход.

## Пороги оповещений

Хранятся в `%APPDATA%\Gradusnik\settings.json`. Изменения применяются сразу после сохранения файла, перезапуск не нужен.

```json
{
  "AlertsEnabled": true,
  "CpuAlert": 85,
  "GpuAlert": 83,
  "GpuHotSpotAlert": 100
}
```

Датчики читаются через [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).

## Требования

- Windows 10/11 x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Драйвер [PawnIO](https://github.com/namazso/PawnIO.Setup/releases) для датчиков CPU: `winget install namazso.PawnIO`
- Права администратора: программа запрашивает их при запуске

## Сборка

```
dotnet publish -c Release -o "%LOCALAPPDATA%\Programs\Gradusnik"
```

Нужен .NET 8 SDK: `winget install Microsoft.DotNet.SDK.8`
