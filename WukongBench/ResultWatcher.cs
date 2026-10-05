namespace WukongBench;

using PathSet = HashSet<string>;

/// <summary>
/// Отслеживает появление новых файлов результатов в %TEMP%\b1\BenchMarkHistory\Tool\&lt;unix-время&gt;.
/// Папка одна на все запуски, поэтому после каждого прогона снимок «уже виденных» файлов обновляется.
/// </summary>
public sealed class ResultWatcher : IDisposable
{
    private readonly string _historyDir;
    private readonly PathSet _seen = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private readonly object _lock = new();

    public ResultWatcher(string historyDir)
    {
        _historyDir = historyDir;
        Directory.CreateDirectory(historyDir);
        foreach (var f in Directory.GetFiles(historyDir)) _seen.Add(f);
    }

    /// <summary>Запускает фоновое наблюдение за появлением новых файлов в папке истории.</summary>
    public void Start()
    {
        _watcher = new FileSystemWatcher(_historyDir)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => NoteIfSeen(e.FullPath);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Непосредственно после старта прогона: запоминаем, что папка уже могла содержать файлы.</summary>
    public void NoteSeen()
    {
        lock (_lock)
            foreach (var f in Directory.GetFiles(_historyDir)) _seen.Add(f);
    }

    /// <summary>
    /// Ищет среди ранее не виденных файлов валидный результат бенчмарка.
    /// Файл, который ещё дописывается, не распарсится — вернёт null, и мы проверим на следующем шаге.
    /// </summary>
    public (string Path, BenchmarkResult Result)? TryTakeNew()
    {
        lock (_lock)
        {
            foreach (var file in Directory.GetFiles(_historyDir))
            {
                if (_seen.Contains(file)) continue;
                if (!ForensicFiles.IsBenchmarkResultFile(file))
                {
                    _seen.Add(file); // не наше — больше не проверяем
                    continue;
                }

                var result = BenchmarkResult.TryLoad(file);
                if (result is null) continue; // ещё дописывается — проверим позже

                _seen.Add(file);
                return (file, result);
            }
            return null;
        }
    }

    private void NoteIfSeen(string path) => _seen.Add(path);
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