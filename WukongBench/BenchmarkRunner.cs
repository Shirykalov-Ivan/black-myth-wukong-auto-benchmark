using System.Diagnostics;

namespace WukongBench;

/// <summary>
/// Запуск бенчмарка, «слепое» прохождение меню эмуляцией ввода и ожидание файла результата.
/// Требует запущенного Steam: у бенчмарка есть проверка прав (entitlement check) через Steam API.
/// </summary>
public sealed class BenchmarkRunner(string historyDir, Action<string> log)
{
    private static readonly string[] ProcessNames = ["b1", "b1-Win64-Shipping", "b1_benchmark"];

    // Общее время ожидания: 2 прохода × ~2–3 мин на сам тест + меню + загрузка уровней.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StepDelay = TimeSpan.FromSeconds(3);

    // Доли клиентской области окна, проверены на 1280×720, 1600×900 и 1920×1080.
    private const double StartButtonX = 0.10, StartButtonY = 0.45;     // «Тест быстродействия»
    private const double ConfirmButtonX = 0.39, ConfirmButtonY = 0.59; // «Подтвердить»

    /// <summary>Запускает прогон и возвращает найденный результат вместе с путём к его raw-файлу.</summary>
    public (string RawPath, BenchmarkResult Result) Run(string exePath)
    {
        if (FindBenchmarkProcesses().Any())
            throw new InvalidOperationException("Бенчмарк уже запущен. Закройте его и повторите запуск.");

        // Снимок перед стартом: файлы, оставшиеся от прошлых запусков, результатом не считаем.
        using var watcher = new ResultWatcher(historyDir);
        watcher.NoteSeen();
        watcher.Start();

        Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath),
        })?.Dispose();

        log("Бенчмарк запущен. Прохожу меню (в это время не трогайте мышь и клавиатуру).");

        var stopwatch = Stopwatch.StartNew();
        var step = 0;
        try
        {
            while (stopwatch.Elapsed < Timeout)
            {
                // Проверяем результат до каждого действия: на экране результатов есть
                // кнопка «Пройти заново» — кликать по ней нельзя.
                var finished = watcher.TryTakeNew();
                if (finished is not null) return finished.Value;

                var window = FindGameWindow();
                if (window != IntPtr.Zero)
                {
                    if (!NativeInput.IsForeground(window)) NativeInput.TryActivate(window);

                    // Кликаем только по собственному окну, иначе можно попасть в чужое приложение.
                    if (NativeInput.IsForeground(window)) DoMenuStep(window, step++);
                }

                Thread.Sleep(StepDelay);
            }
            throw new TimeoutException($"Бенчмарк не выдал результат за {Timeout.TotalMinutes} мин. Проверьте, что Steam запущен и приложение установлено.");
        }
        finally
        {
            KillBenchmark();
        }
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

    private static void KillBenchmark()
    {
        // Убиваем, а не закрываем штатно: так игра не перезапишет наш ini при выходе.
        foreach (var process in FindBenchmarkProcesses())
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