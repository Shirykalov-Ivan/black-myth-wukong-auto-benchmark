using System.Runtime.InteropServices;

namespace WukongBench;

/// <summary>
/// Win32-обёртка для работы с окном бенчмарка: активация, клики по относительным координатам, клавиши.
/// Работает только на Windows (WinAPI).
/// </summary>
public static class NativeInput
{
    public const ushort VkReturn = 0x0D;

    /// <summary>Доступна ли эмуляция ввода на этой платформе (только нативные Windows).</summary>
    public static bool Supported => OperatingSystem.IsWindows();

    public static bool IsForeground(IntPtr window) => GetForegroundWindow() == window;

    public static bool TryActivate(IntPtr window) => SetForegroundWindow(window);

    /// <summary>
    /// Без этого на экранах с DPI-масштабом 125%/150% координаты курсора будут смещены,
    /// и клики попадут мимо кнопок меню.
    /// </summary>
    public static void EnableDpiAwareness() => SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2

    /// <summary>Клик левой кнопкой мыши в точке, заданной долями клиентской области окна (0..1).</summary>
    public static bool ClickRelative(IntPtr window, double relX, double relY)
    {
        if (!GetClientRect(window, out var rect) || rect.Right == 0 || rect.Bottom == 0) return false;

        var point = new Point { X = (int)(rect.Right * relX), Y = (int)(rect.Bottom * relY) };
        if (!ClientToScreen(window, ref point)) return false;

        SetCursorPos(point.X, point.Y);
        Thread.Sleep(150); // меню реагирует на элемент под курсором — даём ему «увидеть» наведение
        Send(MouseInput(MouseLeftDown));
        Thread.Sleep(50);
        Send(MouseInput(MouseLeftUp));
        return true;
    }

    public static void PressKey(ushort virtualKey)
    {
        Send(KeyInput(virtualKey, 0));
        Thread.Sleep(50);
        Send(KeyInput(virtualKey, KeyUp));
    }

    private static void Send(Input input) => SendInput(1, [input], Marshal.SizeOf<Input>());

    private static Input MouseInput(uint flags) =>
        new() { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInputData { Flags = flags } } };

    private static Input KeyInput(ushort vk, uint flags) =>
        new() { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInputData { VirtualKey = vk, Flags = flags } } };

    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, KeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData { public int Dx, Dy; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData { public ushort VirtualKey, ScanCode; public uint Flags, Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}