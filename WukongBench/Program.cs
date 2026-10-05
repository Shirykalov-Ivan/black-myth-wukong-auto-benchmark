using System.Runtime.InteropServices;
using System.Text;

namespace WukongBench;

/// <summary>Точка входа: автоопределение установки, два прохода, сбор и вывод отчёта.</summary>
public static class Program
{
    public const string AppId = "3132990";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("══════════════════════════════════════════════");
        Console.WriteLine("  WukongBench — автоматический запуск бенчмарка");
        Console.WriteLine("  Black Myth: Wukong Benchmark Tool (Steam)");
        Console.WriteLine("══════════════════════════════════════════════");

        if (args.Contains("--demo"))
        {
            var demoDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WukongBench", "results-demo", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(demoDir);
            using (var log = new StreamWriter(Path.Combine(demoDir, "run.log"), append: false, Encoding.UTF8))
            {
                Console.SetOut(new TeeTextWriter(Console.Out, log));
                Console.SetError(new TeeTextWriter(Console.Error, log));
                return RunDemo(demoDir);
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Этот инструмент может работать только на нативных Windows: прохождение меню бенчмарка выполняется эмуляцией ввода через Win32 API.");
            return 2;
        }

        if (!NativeInput.Supported)
        {
            Console.Error.WriteLine("Эмуляция ввода недоступна на этой платформе.");
            return 2;
        }

        try
        {
            NativeInput.EnableDpiAwareness();

            var benchmarkDir = args.Length > 0 ? args[0] : SteamGameLocator.Locate(AppId);
            var exePath = FindExe(benchmarkDir);
            var iniPath = Path.Combine(benchmarkDir, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini");
            var historyDir = Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");

            Console.WriteLine($"Директория бенчмарка:  {benchmarkDir}");
            Console.WriteLine($"Исполняемый файл:       {exePath}");
            Console.WriteLine($"Файл настроек:          {iniPath}");
            Console.WriteLine($"Папка результатов:      {historyDir}");
            Console.WriteLine();
            Console.WriteLine("Проверьте: Steam запущен и вы владеете Black Myth: Wukong Benchmark Tool.");
            Console.WriteLine("Во время прогона не трогайте мышь и клавиатуру. Нажмите Enter, когда будете готовы.");
            Console.ReadLine();

            var settings = new SettingsFile(iniPath, Console.WriteLine);
            settings.RecoverAfterCrash();

            var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WukongBench", "results", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outputDir);

            // Весь вывод консоли дублируется в журнал прогона.
            using (var log = new StreamWriter(Path.Combine(outputDir, "run.log"), append: false, Encoding.UTF8))
            {
                Console.SetOut(new TeeTextWriter(Console.Out, log));
                Console.SetError(new TeeTextWriter(Console.Error, log));
                return RunMain(args, benchmarkDir, exePath, iniPath, historyDir, outputDir, settings);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Ошибка: {e.Message}");
            Console.Error.WriteLine("Подробности: см. stack trace ниже.");
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    /// <summary>Основной сценарий: два прохода бенчмарка и итоговый отчёт.</summary>
    private static int RunMain(
        string[] args,
        string benchmarkDir,
        string exePath,
        string iniPath,
        string historyDir,
        string outputDir,
        SettingsFile settings)
    {
        var system = SystemInfo.Collect();
        var runner = new BenchmarkRunner(historyDir, Console.WriteLine);
        var results = new List<(BenchmarkProfile Profile, BenchmarkResult Result)>();

        settings.Backup();
        Console.CancelKeyPress += (_, _) => SafeRestore(settings);
        try
        {
            foreach (var profile in new[] { BenchmarkProfile.Cpu, BenchmarkProfile.Gpu })
            {
                Console.WriteLine();
                Console.WriteLine($"[{profile.Name}-тест] Применяю настройки и запускаю бенчмарк...");
                settings.Apply(profile);

                var (rawPath, result) = runner.Run(exePath);
                File.Copy(rawPath, Path.Combine(outputDir, $"{profile.Name.ToLowerInvariant()}_raw.json"));
                results.Add((profile, result));

                Console.WriteLine($"[{profile.Name}-тест] Готово: средний FPS {result.FPSAvg:0}, доля кадров, где CPU дольше GPU: {result.CpuBoundShare:0}%");
            }
        }
        finally
        {
            settings.Restore();
        }

        var report = ReportPrinter.Build(system, results);
        Console.WriteLine();
        Console.WriteLine(report);
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), report, new UTF8Encoding(true));

        Console.WriteLine($"Отчёт сохранён: {Path.Combine(outputDir, "report.txt")}");
        Console.WriteLine("Нажмите Enter для выхода.");
        Console.ReadLine();
        return 0;
    }

    private static string FindExe(string benchmarkDir)
    {
        string[] candidates =
        [
            Path.Combine(benchmarkDir, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe"),
            Path.Combine(benchmarkDir, "b1.exe"),
            Path.Combine(benchmarkDir, "b1_benchmark.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"Не найден исполняемый файл бенчмарка в {benchmarkDir}. Передайте путь к папке установки первым аргументом.");
    }

    private static void SafeRestore(SettingsFile settings)
    {
        try
        {
            settings.Restore();
            Console.WriteLine("Конфиг восстановлен, можно закрыть окно.");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Не удалось восстановить конфиг: {e.Message}");
        }
    }

    /// <summary>
    /// Демонстрационный режим: не запускает бенчмарк, а строит отчёт на синтетических данных
    /// (чтобы показать формат вывода без установленного Benchmark Tool). Отчёт явно помечен как demo.
    /// </summary>
    private static int RunDemo(string demoDir)
    {
        Console.WriteLine("Демонстрационный режим (без запуска Benchmark Tool).");
        Console.WriteLine("Отчёт построен на синтетических данных и ЯВНО помечен как демонстрация.");
        Console.WriteLine();

        var system = SystemInfo.Collect();

        var demo = new List<(BenchmarkProfile Profile, BenchmarkResult Result)>
        {
            (BenchmarkProfile.Cpu, DemoResult(system, BenchmarkProfile.Cpu)),
            (BenchmarkProfile.Gpu, DemoResult(system, BenchmarkProfile.Gpu)),
        };

        var report = ReportPrinter.Build(system, demo);
        var demoNotice = new StringBuilder();
        demoNotice.AppendLine("┌──────────────────────────────────────────────────────────────────────────┐");
        demoNotice.AppendLine("│ ДЕМОНСТРАЦИОННЫЙ РЕЖИМ: результаты СИНТЕТИЧЕСКИЕ, бенчмарк не запускался. │");
        demoNotice.AppendLine("│ Запуск бенчмарка:  WukongBench.exe  (без --demo).                        │");
        demoNotice.AppendLine("└──────────────────────────────────────────────────────────────────────────┘");

        Console.WriteLine(demoNotice.ToString());
        Console.WriteLine(report);

        var demoPath = Path.Combine(demoDir, "report-demo.txt");
        File.WriteAllText(demoPath, demoNotice + "\n\n" + report, new UTF8Encoding(true));
        Console.WriteLine($"Отчёт сохранён: {demoPath}");
        Console.WriteLine("Нажмите Enter для выхода.");
        Console.ReadLine();
        return 0;
    }

    /// <summary>Синтетический результат с реалистичными значениями для демонстрации отчёта.</summary>
    private static BenchmarkResult DemoResult(SystemInfo system, BenchmarkProfile profile)
    {
        var rng = new Random(42);
        const int frameCount = 3000;
        var records = new List<FrameRecord>(frameCount);

        // Базовая нагрузка: у CPU-профиля кадр чаще считает CPU, у GPU-профиля — GPU.
        double targetFps = profile.Name == "CPU" ? 138.0 : 96.0;
        double cpuTime, gpuTime;
        if (profile.Name == "CPU")
        {
            cpuTime = 9.2; // CPU-лимит
            gpuTime = 2.8;
        }
        else
        {
            cpuTime = 4.5;
            gpuTime = 10.1; // GPU-лимит
        }

        for (var i = 0; i < frameCount; i++)
        {
            var st = rng.NextDouble();
            var frameRate = targetFps * (0.85 + 0.30 * st);
            var cpuUsage = 55 + 35 * rng.NextDouble();
            var gpuUsage = (profile.Name == "CPU" ? 25.0 : 92.0) + 7 * rng.NextDouble();
            records.Add(new FrameRecord
            {
                FrameRate = frameRate,
                CPUUsage = cpuUsage,
                GPUUsage = gpuUsage,
                CPUFrameTime = cpuTime * (0.85 + 0.30 * rng.NextDouble()),
                GPUFrameTime = gpuTime * (0.85 + 0.30 * rng.NextDouble()),
                VideoMemoryUsage = profile.Name == "CPU" ? 1.2 + rng.NextDouble() : 10.5 + rng.NextDouble(),
            });
        }

        var sorted = records.Select(r => r.FrameRate).OrderBy(v => v).ToList();
        double p95 = sorted[(int)(sorted.Count * 0.05)];
        double avg = sorted.Average();
        double min = sorted.First();
        double max = sorted.Last();

        return new BenchmarkResult
        {
            GameVer = "1.0.8.14860",
            SysVer = RuntimeInformation.OSDescription,
            CPUModel = system?.Cpu ?? "неизвестно",
            GPUModel = system?.Gpus.FirstOrDefault() ?? "неизвестно",
            GpuDriverVer = "31.0.12029.1001",
            CPUAvg = records.Average(r => r.CPUUsage),
            GPUAvg = records.Average(r => r.GPUUsage),
            FPSAvg = avg,
            FPSMin = min,
            FPSMax = max,
            FPS95 = p95,
            VideoMem = records.Average(r => r.VideoMemoryUsage),
            VideoMemSize = "4 ГБ",
            ScreenMode = 1,
            ScreenResolution = profile.ExpectedResolution,
            QualityLevel = profile.ExpectedQualityLevel,
            ImageQuality = profile.Name == "CPU" ? 50 : 100,
            ViewDistance = profile.ExpectedQualityLevel,
            AntiAliasing = profile.ExpectedQualityLevel,
            PostProcessing = profile.ExpectedQualityLevel,
            ShadowQuality = profile.ExpectedQualityLevel,
            TextureQuality = profile.ExpectedQualityLevel,
            MaterialQuality = profile.ExpectedQualityLevel,
            VegetationQuality = profile.ExpectedQualityLevel,
            MotionBlur = 0,
            Rtx = 0,
            Dlss = 3,
            InsertFrame = 0,
            Dx12 = 1,
            Records = records,
        };
    }

    /// <summary>Текстовый писатель, дублирующий вывод сразу в два потока (консоль + файл лога).</summary>
    private sealed class TeeTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
    {
        public override Encoding Encoding => primary.Encoding;

        public override void Write(char value)
        {
            primary.Write(value);
            secondary.Write(value);
        }

        public override void Write(string? value)
        {
            primary.Write(value);
            secondary.Write(value);
        }

        public override void WriteLine(string? value)
        {
            primary.WriteLine(value);
            secondary.WriteLine(value);
        }

        public override void Flush()
        {
            primary.Flush();
            secondary.Flush();
        }
    }
}