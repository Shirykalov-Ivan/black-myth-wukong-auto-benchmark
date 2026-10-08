using System.Text;

namespace WukongBench;

/// <summary>
/// Формирование итогового отчёта: характеристики ПК, результаты обоих проходов и использованные настройки.
/// Формат — аккуратные выровненные колонки, удобные для чтения в консоли и в .txt.
/// </summary>
public static class ReportPrinter
{
    public static string Build(SystemInfo system, IReadOnlyList<(BenchmarkProfile Profile, BenchmarkResult Result)> runs)
    {
        var sb = new StringBuilder();

        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine("  Отчёт Black Myth: Wukong Benchmark Tool (автоматический)");
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("── Характеристики компьютера ──");
        sb.AppendLine($"Компьютер: {system.MachineName}");
        sb.AppendLine($"ОС:        {system.Os}");
        sb.AppendLine($"CPU:       {system.Cpu} ({system.LogicalCores} логических ядер)");
        sb.AppendLine($"RAM:       {system.RamGb:0.#} ГБ");
        foreach (var gpu in system.Gpus) sb.AppendLine($"GPU:       {gpu}");
        if (system.Gpus.Count == 0) sb.AppendLine("GPU:       не определена (нет доступа к WMI)");
        if (runs.Count > 0)
        {
            var r = runs[0].Result;
            if (!string.IsNullOrWhiteSpace(r.CPUModel) || !string.IsNullOrWhiteSpace(r.GPUModel))
                sb.AppendLine($"Бенчмарк видит: {r.CPUModel} / {r.GPUModel} ({r.VideoMemSize}), версия {r.GameVer}");
        }

        sb.AppendLine();
        sb.AppendLine("── Результаты ──");
        Row(sb, "Метрика", runs.Select(x => x.Profile.Name + "-тест"));
        Row(sb, "FPS (средний)", runs.Select(x => $"{x.Result.FPSAvg:0}"));
        Row(sb, "FPS (минимум)", runs.Select(x => $"{x.Result.FPSMin:0}"));
        Row(sb, "FPS (максимум)", runs.Select(x => $"{x.Result.FPSMax:0}"));
        Row(sb, "FPS (5-й процентиль)", runs.Select(x => $"{x.Result.FPS95:0}"));
        Row(sb, "Загрузка CPU, %", runs.Select(x => $"{x.Result.CPUAvg:0}"));
        Row(sb, "Загрузка GPU, %", runs.Select(x => $"{x.Result.GPUAvg:0}"));
        Row(sb, "Время кадра CPU, мс", runs.Select(x => $"{x.Result.AvgCpuFrameTime:0.00}"));
        Row(sb, "Время кадра GPU, мс", runs.Select(x => $"{x.Result.AvgGpuFrameTime:0.00}"));
        Row(sb, "Кадров, где CPU дольше GPU, %", runs.Select(x => $"{x.Result.CpuBoundShare:0}"));
        Row(sb, "Кадров, где GPU дольше CPU, %", runs.Select(x => $"{x.Result.GpuBoundShare:0}"));
        Row(sb, "Видеопамять, ГБ", runs.Select(x => $"{x.Result.VideoMem:0.00}"));

        sb.AppendLine();
        sb.AppendLine("── Настройки (по данным бенчмарка) ──");
        Row(sb, "Параметр", runs.Select(x => x.Profile.Name + "-тест"));
        Row(sb, "Разрешение", runs.Select(x => x.Result.ScreenResolution));
        Row(sb, "Масштаб рендеринга, %", runs.Select(x => x.Result.ImageQuality.ToString()));
        Row(sb, "Общий уровень качества", runs.Select(x => x.Result.QualityLevel.ToString()));
        Row(sb, "Дальность прорисовки", runs.Select(x => x.Result.ViewDistance.ToString()));
        Row(sb, "Сглаживание", runs.Select(x => x.Result.AntiAliasing.ToString()));
        Row(sb, "Постобработка", runs.Select(x => x.Result.PostProcessing.ToString()));
        Row(sb, "Тени", runs.Select(x => x.Result.ShadowQuality.ToString()));
        Row(sb, "Текстуры", runs.Select(x => x.Result.TextureQuality.ToString()));
        Row(sb, "Материалы", runs.Select(x => x.Result.MaterialQuality.ToString()));
        Row(sb, "Растительность", runs.Select(x => x.Result.VegetationQuality.ToString()));
        Row(sb, "Апскейлер (код)", runs.Select(x => x.Result.Dlss.ToString()));
        Row(sb, "Генерация кадров", runs.Select(x => OnOff(x.Result.InsertFrame)));
        Row(sb, "Трассировка лучей", runs.Select(x => OnOff(x.Result.Rtx)));
        Row(sb, "Размытие в движении", runs.Select(x => OnOff(x.Result.MotionBlur)));

        // Игра может молча подставить другие значения — проверяем, что применилось задуманное.
        var warnings = new List<string>();
        foreach (var (profile, result) in runs)
        {
            if (!string.IsNullOrEmpty(result.ScreenResolution) && result.ScreenResolution != profile.ExpectedResolution)
                warnings.Add($"[{profile.Name}] Разрешение по данным теста: {result.ScreenResolution}, ожидалось {profile.ExpectedResolution}.");
            if (result.QualityLevel != 0 && result.QualityLevel != profile.ExpectedQualityLevel)
                warnings.Add($"[{profile.Name}] Уровень качества по данным теста: {result.QualityLevel}, ожидался {profile.ExpectedQualityLevel}.");
        }
        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("── ВНИМАНИЕ: применённые настройки могли отличаться от задуманных ──");
            foreach (var w in warnings) sb.AppendLine(w);
            sb.AppendLine("Возможные причины: игра не перечитала конфиг, или настройки были изменены вручную.");
        }

        sb.AppendLine();
        sb.AppendLine("── Обоснование выбранных настроек ──");
        foreach (var (profile, _) in runs)
        {
            sb.AppendLine();
            sb.AppendLine($"● {profile.Name}-тест:");
            foreach (var line in profile.Rationale.Split('\n'))
                sb.AppendLine($"    {line.Trim()}");
        }

        return sb.ToString();
    }

    private static string OnOff(int value) => value == 0 ? "выкл" : $"вкл ({value})";

    private static void Row(StringBuilder sb, string name, IEnumerable<string> values) =>
        sb.AppendLine($"{name,-30}" + string.Concat(values.Select(v => $"{v,16}")));
}