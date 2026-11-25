using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Nexer.Hosting.Platform;

/// <summary>Windows console: VT input/output modes, UTF-8 code pages, record-based VT input.</summary>
/// <remarks>
/// Input is read with <c>ReadConsoleInputW</c> after a bounded wait so the read loop can observe cancellation;
/// with ENABLE_VIRTUAL_TERMINAL_INPUT every key record carries the characters of the VT byte stream.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsConsolePlatform : ITerminalPlatform
{
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private const uint EnableProcessedOutput = 0x0001;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint DisableNewlineAutoReturn = 0x0008;
    private const uint Utf8CodePage = 65001;
    private const ushort KeyEventType = 0x0001;
    private const ushort VkMenu = 0x12;
    private const uint WaitObject0 = 0;

    private readonly nint input;
    private readonly nint output;
    private readonly InputRecord[] records = new InputRecord[128];
    private readonly char[] chars = new char[256];
    private readonly Encoder encoder = new UTF8Encoding(false).GetEncoder();
    private readonly Lock gate = new();
    private uint originalInputMode;
    private uint originalOutputMode;
    private uint originalInputCodePage;
    private uint originalOutputCodePage;
    private bool entered;

    public WindowsConsolePlatform()
    {
        input = GetStdHandle(-10);
        output = GetStdHandle(-11);
        if (!GetConsoleMode(input, out originalInputMode) || !GetConsoleMode(output, out originalOutputMode))
        {
            throw new InvalidOperationException(
                "ConsoleTerminalHost requires an interactive console; standard input or output is redirected or not a console.");
        }
    }

    public void Enter()
    {
        lock (gate)
        {
            if (entered) return;
            GetConsoleMode(input, out originalInputMode);
            GetConsoleMode(output, out originalOutputMode);
            originalInputCodePage = GetConsoleCP();
            originalOutputCodePage = GetConsoleOutputCP();
            uint outputMode = originalOutputMode | EnableProcessedOutput | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn;
            if (!SetConsoleMode(output, outputMode) &&
                !SetConsoleMode(output, originalOutputMode | EnableProcessedOutput | EnableVirtualTerminalProcessing))
            {
                throw new PlatformNotSupportedException("This console does not support virtual terminal output (Windows 10 1809 or later is required).");
            }

            // Quick-edit must be off or the console consumes mouse input for text selection. Mouse records are
            // off too: mouse input arrives as VT sequences once the host enables xterm mouse reporting.
            uint inputMode = (originalInputMode & ~(EnableProcessedInput | EnableLineInput | EnableEchoInput | EnableQuickEditMode | EnableMouseInput))
                | EnableExtendedFlags | EnableVirtualTerminalInput | EnableWindowInput;
            if (!SetConsoleMode(input, inputMode))
            {
                SetConsoleMode(output, originalOutputMode);
                throw new PlatformNotSupportedException("This console does not support virtual terminal input.");
            }

            SetConsoleCP(Utf8CodePage);
            SetConsoleOutputCP(Utf8CodePage);
            entered = true;
        }
    }

    public void Restore()
    {
        lock (gate)
        {
            if (!entered) return;
            entered = false;
            SetConsoleMode(input, originalInputMode);
            SetConsoleMode(output, originalOutputMode);
            if (originalInputCodePage != 0) SetConsoleCP(originalInputCodePage);
            if (originalOutputCodePage != 0) SetConsoleOutputCP(originalOutputCodePage);
        }
    }

    public TerminalSize GetSize()
    {
        if (!GetConsoleScreenBufferInfo(output, out ConsoleScreenBufferInfo info)) return new TerminalSize(80, 24);
        return new TerminalSize(Math.Max(1, info.WindowRight - info.WindowLeft + 1), Math.Max(1, info.WindowBottom - info.WindowTop + 1));
    }

    public unsafe void Write(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* start = bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                if (!WriteFile(output, start + offset, bytes.Length - offset, out int written, 0) || written <= 0)
                {
                    throw new IOException("Writing to the console failed.", Marshal.GetHRForLastWin32Error());
                }

                offset += written;
            }
        }
    }

    public unsafe int Read(Span<byte> buffer, TimeSpan timeout)
    {
        uint wait = WaitForSingleObject(input, (uint)Math.Clamp((long)Math.Ceiling(timeout.TotalMilliseconds), 0, 1000));
        if (wait != WaitObject0) return 0;
        if (!GetNumberOfConsoleInputEvents(input, out uint available) || available == 0) return 0;
        int read;
        fixed (InputRecord* start = records)
        {
            if (!ReadConsoleInputW(input, start, (int)Math.Min(available, (uint)records.Length), out read)) return -1;
        }

        int charCount = 0;
        for (int i = 0; i < read; i++)
        {
            ref InputRecord record = ref records[i];
            if (record.EventType != KeyEventType || record.UnicodeChar == 0) continue;
            // Alt+numpad composition delivers its character on the Alt key-up.
            if (record.KeyDown == 0 && record.VirtualKeyCode != VkMenu) continue;
            for (int r = Math.Max((int)record.RepeatCount, 1); r > 0 && charCount < chars.Length; r--) chars[charCount++] = (char)record.UnicodeChar;
        }

        return charCount == 0 ? 0 : encoder.GetBytes(chars.AsSpan(0, charCount), buffer, flush: false);
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(8)] public ushort RepeatCount;
        [FieldOffset(10)] public ushort VirtualKeyCode;
        [FieldOffset(14)] public ushort UnicodeChar;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ConsoleScreenBufferInfo
    {
        public short SizeX, SizeY, CursorX, CursorY;
        public ushort Attributes;
        public short WindowLeft, WindowTop, WindowRight, WindowBottom;
        public short MaxX, MaxY;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleCP();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleOutputCP();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCP(uint codePage);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleOutputCP(uint codePage);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(nint handle, out ConsoleScreenBufferInfo info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WriteFile(nint handle, byte* buffer, int count, out int written, nint overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNumberOfConsoleInputEvents(nint handle, out uint count);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadConsoleInputW(nint handle, InputRecord* buffer, int length, out int read);
}
