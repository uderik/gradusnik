# Градусник (Gradusnik)

Маленькая утилита для Windows 11: показывает температуру CPU и GPU цифрами прямо в трее.

- Две иконки: **синяя** — CPU, **зелёная** — GPU. От 70°C цифра становится оранжевой, от 85°C — красной.
- Подсказка при наведении: загрузка, потребление в ваттах, hot spot видеокарты, температура SSD.
- Правый клик: автозапуск при входе в Windows (через Планировщик заданий, без запроса UAC) и выход.

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
