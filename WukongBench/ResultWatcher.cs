namespace WukongBench;

using PathSet = HashSet<string>;

/// <summary>
/// Отслеживает появление новых файлов результатов. Опрос каталога (polling) вместо FileSystemWatcher:
/// событие Created срабатывает в момент создания файла, но бенчмарк ещё дописывает в него ~10 000 записей —
/// на готовый, валидный JSON можно наткнуться только перечитывая файлы, пока не распарсится.
/// </summary>
public sealed class ResultWatcher
{
    private readonly IReadOnlyList<string> _searchDirs;
    private readonly PathSet _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ResultWatcher(IReadOnlyList<string> historyDirs, IEnumerable<string>? preExisting = null)
    {
        _searchDirs = historyDirs;
        if (preExisting is not null)
            foreach (var f in preExisting) _seen.Add(f);
    }

    /// <summary>Пометить как «уже виденные» все текущие файлы во всех папках (снимок перед стартом).</summary>
    public void MarkAllExistingAsSeen()
    {
        lock (_lock)
            foreach (var dir in _searchDirs)
                foreach (var f in SafeGetFiles(dir))
                    _seen.Add(f);
    }

    /// <summary>
    /// Ищет среди ранее не виденных файлов валидный результат бенчмарка.
    /// Файл, который ещё дописывается, не распарсится — вернёт null, и мы проверим на следующем шаге.
    /// </summary>
    public (string Path, BenchmarkResult Result)? TryTakeNew()
    {
        lock (_lock)
        {
            foreach (var dir in _searchDirs)
            {
                foreach (var file in SafeGetFiles(dir))
                {
                    if (_seen.Contains(file)) continue;

                    if (!ForensicFiles.IsBenchmarkResultFile(file))
                    {
                        _seen.Add(file); // не наше — больше не проверяем
                        continue;
                    }

                    var result = BenchmarkResult.TryLoad(file);
                    if (result is null) continue; // ещё дописывается или битый — проверим позже

                    _seen.Add(file);
                    return (file, result);
                }
            }
            return null;
        }
    }

    private static IEnumerable<string> SafeGetFiles(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir))
            yield return f;
    }
}

/// <summary>
/// Распознавание «наших» файлов результатов: в папке истории могут лежать чужие JSON (логи и т.п.),
/// поэтому файл считается результатом бенчмарка, только если содержит ключевые поля метрик.
/// </summary>
public static class ForensicFiles
{
    private static readonly string[] RequiredKeys = ["FPSAvg", "FPSMin", "Records"];

    public static bool IsBenchmarkResultFile(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            foreach (var key in RequiredKeys)
                if (!content.Contains($"\"{key}\"", StringComparison.Ordinal)) return false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}