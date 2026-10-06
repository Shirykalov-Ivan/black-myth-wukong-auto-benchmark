namespace WukongBench;

/// <summary>
/// Находит папку(и), куда Benchmark Tool пишет результаты. Точное расположение зависит от версии,
/// локальных путей и того, как игра была запущена, поэтому ищем по нескольким кандидатам:
///   • %TEMP%\b1\BenchMarkHistory\Tool              (классическая, изначальная)
///   • %LOCALAPPDATA%\b1\Saved\...BenchMarkHistory   (тоже встречается)
///   • b1\Saved\... внутри папки установки
/// Если кандидаты пусты — сканируем целиком Install-папку и %LOCALAPPDATA%\b1 в поисках
/// каталога, имя которого равно unix-времени запуска результат-файла (шаблон бенчмарка).
/// Выбираем не одну, а ВСЕ найденные папки: бенчмарк мог сохранить результат в любую из них.
/// </summary>
public static class ResultLocator
{
    /// <summary>Песочница определений кандидатов.</summary>
    public static List<string> GetCandidateDirs(string benchmarkDir)
    {
        var result = new List<string>();

        var tempBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); // %LOCALAPPDATA%
        if (string.IsNullOrEmpty(tempBase))
            tempBase = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";

        var candidates = new List<string?>
        {
            // классика
            Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool"),
            // локальные данные приложения
            string.IsNullOrEmpty(tempBase) ? null : Path.Combine(tempBase, "b1", "BenchMarkHistory", "Tool"),
            string.IsNullOrEmpty(tempBase) ? null : Path.Combine(tempBase, "b1", "Saved", "BenchMarkHistory", "Tool"),
        };

        // внутри папки установки — если игра пишет туда
        foreach (var rel in new[]
        {
            Path.Combine("b1", "Saved", "BenchMarkHistory", "Tool"),
            Path.Combine("b1", "Saved", "Config", "BenchMarkHistory", "Tool"),
            Path.Combine("b1", "BenchMarkHistory", "Tool"),
        })
        {
            candidates.Add(Path.Combine(benchmarkDir, rel));
        }

        foreach (var c in candidates)
        {
            if (c is not null && !result.Contains(c, StringComparer.OrdinalIgnoreCase))
                result.Add(c);
        }

        return result;
    }

    /// <summary>Оставляет только существующие папки-кандидаты; если их нет — сканирует шире.</summary>
    public static List<string> FindExistingDirs(string benchmarkDir)
    {
        var dirs = GetCandidateDirs(benchmarkDir).Where(Directory.Exists).ToList();
        if (dirs.Count > 0) return dirs;

        // Если ни один кандидат не существует — целенаправленное сканирование.
        foreach (var scanRoot in new[] { Path.Combine(benchmarkDir, "b1", "Saved"), Path.Combine(benchmarkDir, "b1") })
        {
            if (!Directory.Exists(scanRoot)) continue;
            foreach (var dir in Directory.EnumerateDirectories(scanRoot, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("Tool", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("BenchMarkHistory", StringComparison.OrdinalIgnoreCase))
                {
                    if (!dirs.Contains(dir, StringComparer.OrdinalIgnoreCase))
                        dirs.Add(dir);
                }
            }
        }
        return dirs;
    }
}