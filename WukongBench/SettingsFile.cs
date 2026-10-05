using System.Text;
using System.Text.RegularExpressions;

namespace WukongBench;

/// <summary>
/// Работа с GameUserSettings.ini бенчмарка: резервная копия, применение профиля настроек,
/// восстановление, восстановление после аварийного завершения.
/// </summary>
/// <remarks>
/// Настройки хранятся в двух представлениях:
///  1. Строка UISettingData=(("Key","Value"),...) — то, что показывает меню игры;
///  2. Секция [ScalabilityGroups] — низкоуровневые группы качества Unreal Engine,
///     где значение группы равно «значение меню − 1».
/// Изменяются только нужные ключи, остальной файл не трогается.
/// </remarks>
public sealed class SettingsFile(string path, Action<string> log)
{
    private readonly string _backupPath = path + ".wukongbench.bak";

    /// <summary>Восстановление после «убитого» прошлого запуска: если осталась резервная копия — вернуть оригинал.</summary>
    public bool RecoverAfterCrash()
    {
        if (!File.Exists(_backupPath)) return false;
        log("Найдена резервная копия от незавершённого прошлого запуска — восстанавливаю исходный конфиг.");
        Restore();
        return true;
    }

    public void Backup()
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Не найден конфиг бенчмарка: {path}. Запустите 'Black Myth: Wukong Benchmark Tool' вручную хотя бы один раз, чтобы игра создала его.");
        File.Copy(path, _backupPath, overwrite: true);
        log($"Резервная копия конфига: {_backupPath}");
    }

    public void Restore()
    {
        if (!File.Exists(_backupPath)) return;
        File.Copy(_backupPath, path, overwrite: true);
        File.Delete(_backupPath);
        log("Исходный конфиг восстановлен.");
    }

    public void Apply(BenchmarkProfile profile)
    {
        string text;
        Encoding encoding;
        using (var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
            encoding = reader.CurrentEncoding; // UE может писать ini в UTF-16 — сохраняем исходную кодировку
        }

        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

        foreach (var (key, value) in profile.UiSettings)
            SetUiValue(lines, key, value);
        foreach (var ((section, key), value) in profile.IniValues)
            SetIniValue(lines, section, key, value);

        File.WriteAllText(path, string.Join("\r\n", lines), encoding);
    }

    // UISettingData=(("Key", "Value"),("Key2", "Value2"),...)
    private static void SetUiValue(List<string> lines, string key, string value)
    {
        var index = lines.FindIndex(l => l.StartsWith("UISettingData="));
        if (index < 0) throw new InvalidOperationException("В конфиге нет строки UISettingData — конфиг повреждён или от другой версии.");

        var pattern = new Regex($"\\(\"{Regex.Escape(key)}\", \"[^\"]*\"\\)");
        if (!pattern.IsMatch(lines[index]))
            throw new InvalidOperationException($"В UISettingData нет ключа \"{key}\" — конфиг от другой версии игры.");

        lines[index] = pattern.Replace(lines[index], $"(\"{key}\", \"{value}\")");
    }

    private static void SetIniValue(List<string> lines, string section, string key, string value)
    {
        var header = lines.FindIndex(l => l.Trim() == $"[{section}]");
        if (header < 0) throw new InvalidOperationException($"В конфиге нет секции [{section}].");

        for (var i = header + 1; i < lines.Count && !lines[i].StartsWith('['); i++)
        {
            if (lines[i].StartsWith(key + "="))
            {
                lines[i] = $"{key}={value}";
                return;
            }
        }
        lines.Insert(header + 1, $"{key}={value}");
    }
}