using System.Runtime.InteropServices;

namespace WukongBench;

/// <summary>
/// Характеристики компьютера: ОС, процессор, логические ядра, объём ОЗУ и список видеокарт.
/// Собирается из реестра и Win32 API — без сторонних пакетов.
/// </summary>
public sealed record SystemInfo(string MachineName, string Os, string Cpu, int LogicalCores, double RamGb, IReadOnlyList<string> Gpus)
{
    private const string DisplayAdaptersClass =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static SystemInfo Collect()
    {
        var gpus = ReadGpus();
        if (gpus.Count == 0) gpus = ReadGpusLegacy(); // fallback: для старых сборок Windows

        return new SystemInfo(
            Environment.MachineName,
            ReadOs(),
            ReadCpu(),
            Environment.ProcessorCount,
            ReadRamGb(),
            gpus);
    }

    private static string ReadOs()
    {
        if (!OperatingSystem.IsWindows())
            return RuntimeInformation.OSDescription;

        using var key = RegistryKeyBase().OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var name = key?.GetValue("ProductName") as string ?? "Windows";
        var version = key?.GetValue("DisplayVersion") as string ?? "";
        var build = key?.GetValue("CurrentBuild") as string ?? "";

        // На Windows 11 ProductName до сих пор пишет «Windows 10».
        if (int.TryParse(build, out var b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
        return $"{name} {version} (сборка {build})".Trim();
    }

    private static string ReadCpu()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = RegistryKeyBase().OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var value = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "неизвестно";
    }

    private static double ReadRamGb()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Linux: /proc/meminfo
            try
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    var parts = line.Split(':', StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && parts[0] == "MemTotal")
                    {
                        var kb = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        return double.TryParse(kb, out var k) ? k / 1024.0 / 1024 : 0;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
            return 0;
        }

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.TotalPhys / 1024.0 / 1024 / 1024 : 0;
    }

    private static List<string> ReadGpus()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Linux: нативно вытащить точную модель/VRAM сложно, для demo показываем как есть
            return ["видеокарта не определена (не Windows)"];
        }
        var gpus = new List<string>();
        using var root = RegistryKeyBase().OpenSubKey(DisplayAdaptersClass);
        if (root is null) return gpus;

        foreach (var name in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            try
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is not string desc) continue;
                if (gpus.Any(g => g.StartsWith(desc, StringComparison.OrdinalIgnoreCase))) continue; // дедупликация

                var driver = key.GetValue("DriverVersion") as string ?? "?";
                var vramBytes = ReadQword(key, "HardwareInformation.qwMemorySize");
                var vram = vramBytes is > 0 ? $", {vramBytes / 1024.0 / 1024 / 1024:0.#} ГБ VRAM" : "";
                gpus.Add($"{desc}{vram}, драйвер {driver}");
            }
            catch (System.Security.SecurityException) { } // часть подключей закрыта для чтения
        }
        return gpus;
    }

    private static List<string> ReadGpusLegacy()
    {
        var gpus = new List<string>();
        using var key = RegistryKeyBase().OpenSubKey(@"HARDWARE\DEVICEMAP\VIDEO");
        if (key is null) return gpus;
        foreach (var valueName in key.GetValueNames().Where(v => v.StartsWith(@"\Device\Video")))
        {
            if (key.GetValue(valueName) is not string fullPath || string.IsNullOrEmpty(fullPath)) continue;
            var serviceKeyPath = fullPath.Replace(@"\Device\Video", @"SYSTEM\CurrentControlSet\Control\Video");
            using var serviceKey = RegistryKeyBase().OpenSubKey(serviceKeyPath);
            if (serviceKey?.GetValue("Device Description") is not string desc) continue;
            if (gpus.Any(g => g.StartsWith(desc, StringComparison.OrdinalIgnoreCase))) continue;
            gpus.Add($"{desc}, {serviceKey.GetValue("DriverVersion") ?? "?"}");
        }
        return gpus;
    }

    private static long? ReadQword(Microsoft.Win32.RegistryKey key, string name)
    {
        try
        {
            var value = key.GetValue(name);
            switch (value)
            {
                case ulong qw: return (long)qw;
                case long l: return l;
                case byte[] bytes when bytes.Length >= 8: return BitConverter.ToInt64(bytes, 0);
                default: return null;
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or InvalidCastException)
        {
            return null;
        }
    }

    private static Microsoft.Win32.RegistryKey RegistryKeyBase() =>
        Microsoft.Win32.Registry.LocalMachine;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}