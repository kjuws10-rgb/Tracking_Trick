using System.Runtime.InteropServices;

namespace TrackingTrick;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal static class NativeMouse
{
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT { public uint Type; public INPUTUNION Data; }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT Mouse; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int Dx, Dy; public uint MouseData, DwFlags, Time; public nint DwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    internal static void LeftClick()
    {
        var inputs = new[]
        {
            new INPUT { Type = INPUT_MOUSE, Data = new INPUTUNION { Mouse = new MOUSEINPUT { DwFlags = MOUSEEVENTF_LEFTDOWN } } },
            new INPUT { Type = INPUT_MOUSE, Data = new INPUTUNION { Mouse = new MOUSEINPUT { DwFlags = MOUSEEVENTF_LEFTUP } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
