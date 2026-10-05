namespace WukongBench;

/// <summary>
/// Автоопределение папки установки игры в клиенте Steam:
/// читает префикс библиотеки Steam (из HKCU\Software\Valve\Steam\SteamPath),
/// затем libraryfolders.vdf всех библиотек и каталог steamapps\common\Black Myth Wukong Benchmark Tool.
/// Работает и с кастомными библиотеками на других дисках.
/// </summary>
public static class SteamGameLocator
{
    private const string InstallName = "Black Myth Wukong Benchmark Tool";

    public static string Locate(string appId)
    {
        var root = SteamRoot();
        if (string.IsNullOrEmpty(root))
            throw new FileNotFoundException(
                "Не удалось найти Steam: не найден ключ реестра HKCU\\Software\\Valve\\Steam\\SteamPath. " +
                "Передайте путь к папке установки бенчмарка первым аргументом.");

        var libraries = new List<string> { root };
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (var path in ParseLibraryFoldersVdf(vdf))
                libraries.Add(path);
        }

        foreach (var library in libraries)
        {
            var candidate = Path.Combine(library, "steamapps", "common", InstallName);
            if (Directory.Exists(candidate)) return candidate;

            // Каталог приложения может лежать и в отдельной папке manifest-а, но common обычно достаточно.
        }

        // Последняя попытка: если библиотека найдена, но папка common не на месте — попробуем manifest AppID.
        var appManifest = Path.Combine(root, "steamapps", $"appmanifest_{appId}.acf");
        if (File.Exists(appManifest) && TryReadInstallDir(appManifest, root, out var installDir))
            return installDir;

        throw new FileNotFoundException(
            $"Не найден 'Black Myth Wukong Benchmark Tool'. Установите его через Steam и перезапустите. " +
            $"Искали в библиотеках: {string.Join("; ", libraries)}. " +
            "Если он установлен нестандартно — передайте путь первым аргументом.");
    }

    private static string? SteamRoot()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath")?.ToString();
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Парсит libraryfolders.vdf: пути библиотек из значения "path".</summary>
    private static IEnumerable<string> ParseLibraryFoldersVdf(string vdf)
    {
        var result = new List<string>();
        try
        {
            foreach (var line in File.ReadAllLines(vdf))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("\"path\"")) continue;
                var start = trimmed.IndexOf('"', trimmed.IndexOf('"') + 1) + 1;
                var end = trimmed.LastIndexOf('"');
                if (start > 0 && end > start)
                    result.Add(trimmed[start..end]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Библиотека не читается — ищем только в корневой.
        }
        return result;
    }

    private static bool TryReadInstallDir(string manifestPath, string steamRoot, out string installDir)
    {
        installDir = "";
        try
        {
            foreach (var line in File.ReadAllLines(manifestPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("\"installdir\"")) continue;
                var start = trimmed.IndexOf('"', trimmed.IndexOf('"') + 1) + 1;
                var end = trimmed.LastIndexOf('"');
                if (start <= 0 || end <= start) return false;
                installDir = Path.Combine(steamRoot, "steamapps", "common", trimmed[start..end]);
                return Directory.Exists(installDir);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return false;
    }
}