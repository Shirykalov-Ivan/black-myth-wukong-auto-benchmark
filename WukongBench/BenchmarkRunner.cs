using System.Diagnostics;

namespace WukongBench;

/// <summary>
/// Запуск бенчмарка, «слепое» прохождение меню эмуляцией ввода и ожидание файла результата.
/// Требует запущенного Steam: у бенчмарка есть проверка прав (entitlement check) через Steam API.
/// </summary>
public sealed class BenchmarkRunner(string benchmarkDir, string exePath, IReadOnlyList<string> historyDirs, Action<string> log)
{
    // benchmarkDir не используется напрямую — сохранён для будущего расширения диагностики.
    private readonly string _benchmarkDir = benchmarkDir;
    private static readonly string[] ProcessNames = ["b1", "b1-Win64-Shipping", "b1_benchmark"];

    // Общее время ожидания на ОДИН проход: от запуска exe до появления результата.
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(15);

    // Пауза между действиями в меню (3 сек). Итого цикл из 3 шагов — ~9-10 секунд.
    private static readonly TimeSpan StepDelay = TimeSpan.FromSeconds(3);

    // Задержка после запуска процесса: даём UE5 прогрузить движок, окно, меню.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);

    // Пауза между проходами: дождаться полного закрытия окна перед следующей записью конфига.
    private static readonly TimeSpan BetweenRunsDelay = TimeSpan.FromSeconds(10);

    // Доли клиентской области окна, проверены на 1280×720, 1600×900 и 1920×1080.
    private const double StartButtonX = 0.10, StartButtonY = 0.45;     // «Тест быстродействия»
    private const double ConfirmButtonX = 0.39, ConfirmButtonY = 0.59; // «Подтвердить»

    /// <summary>Запускает один проход и возвращает результат + путь к его raw-файлу.</summary>
    public (string RawPath, BenchmarkResult Result) Run()
    {
        EnsureNoBenchmarkRunning();

        // Снимок «уже виденных» файлов перед стартом — их результатом не считаем.
        var watcher = new ResultWatcher(historyDirs);
        watcher.MarkAllExistingAsSeen();

        log($"Запускаю: {exePath}");
        Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath),
        })?.Dispose();

        log("Бенчмарк запущен. Жду загрузки (8 с)...");
        Thread.Sleep(StartupDelay);

        log("Прохожу меню (в это время не трогайте мышь и клавиатуру).");

        var stopwatch = Stopwatch.StartNew();
        var step = 0;
        try
        {
            while (stopwatch.Elapsed < RunTimeout)
            {
                // Проверяем результат до каждого действия: на экране результатов есть
                // кнопка «Пройти заново» — кликать по ней нельзя.
                var finished = watcher.TryTakeNew();
                if (finished is not null)
                {
                    log($"Зафиксирован результат: {finished.Value.Path}");
                    return finished.Value;
                }

                var window = FindGameWindow();
                if (window == IntPtr.Zero)
                {
                    // Окна нет — возможно, бенчмарк уже аварийно закрылся.
                    var stillRunning = FindBenchmarkProcesses().Any();
                    if (!stillRunning)
                        throw new InvalidOperationException(
                            "Процесс бенчмарка завершился раньше времени (краш на старте). Проверьте Steam и целостность установки.");
                    Thread.Sleep(StepDelay);
                    continue;
                }

                if (!NativeInput.IsForeground(window)) NativeInput.TryActivate(window);

                // Кликаем только по собственному окну, иначе можно попасть в чужое приложение.
                if (NativeInput.IsForeground(window)) DoMenuStep(window, step++);

                Thread.Sleep(StepDelay);
            }
            throw new TimeoutException(
                $"Бенчмарк не выдал результат за {RunTimeout.TotalMinutes} мин. Проверьте, что Steam запущен, приложение установлено и экран не заблокирован.");
        }
        finally
        {
            KillBenchmark(allowMissing: false);
        }
    }

    /// <summary>Небольшая задержка между проходами (после kill окна должен полностью закрыться).</summary>
    public void PauseBetweenRuns() => Thread.Sleep(BetweenRunsDelay);

    /// <summary>Убивает все процессы бенчмарка, если они есть (вызывается и извне, в начале/конце).</summary>
    public void KillAnyStale(bool allowMissing)
    {
        KillBenchmark(allowMissing);
    }

    // Слепой цикл: заставка → «Тест быстродействия» → «Подтвердить».
    // Клики во время самого теста его не прерывают (проверено вручную).
    private static void DoMenuStep(IntPtr window, int step)
    {
        switch (step % 3)
        {
            case 0: NativeInput.PressKey(NativeInput.VkReturn); break;
            case 1: NativeInput.ClickRelative(window, StartButtonX, StartButtonY); break;
            case 2: NativeInput.ClickRelative(window, ConfirmButtonX, ConfirmButtonY); break;
        }
    }

    private static IntPtr FindGameWindow() =>
        FindBenchmarkProcesses()
            .Select(p => p.MainWindowHandle)
            .FirstOrDefault(h => h != IntPtr.Zero);

    private static IEnumerable<Process> FindBenchmarkProcesses() =>
        ProcessNames.SelectMany(Process.GetProcessesByName);

    private void EnsureNoBenchmarkRunning()
    {
        var running = FindBenchmarkProcesses().ToList();
        if (running.Count > 0)
            throw new InvalidOperationException(
                "Бенчмарк уже запущен. Закройте его и повторите запуск.");
    }

    private static void KillBenchmark(bool allowMissing)
    {
        // Убиваем, а не закрываем штатно: так игра не перезапишет наш ini при выходе.
        var processes = FindBenchmarkProcesses().ToList();
        if (processes.Count == 0 && allowMissing) return;

        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
                Console.WriteLine("Процесс бенчмарка закрыт.");
            }
            catch (InvalidOperationException) { } // процесс уже завершился
        }
    }
}