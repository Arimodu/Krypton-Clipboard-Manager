using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Serilog;

namespace Krypton_Desktop.Services.Platform;

/// <summary>
/// Windows-specific clipboard listener using AddClipboardFormatListener.
/// Creates a message-only window to receive WM_CLIPBOARDUPDATE notifications.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboardListener : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_DESTROY = 0x0002;
    private const int HWND_MESSAGE = -3;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterClassW([MarshalAs(UnmanagedType.LPWStr)] string lpClassName, IntPtr hInstance);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private readonly string _className;
    private readonly WndProcDelegate _wndProcDelegate;
    private IntPtr _hwnd;
    private Thread? _messageThread;
    private volatile bool _isRunning;
    private bool _isDisposed;

    public event EventHandler? ClipboardChanged;
    public bool IsListening => _hwnd != IntPtr.Zero && _isRunning;

    public WindowsClipboardListener()
    {
        _className = $"KryptonClipboardListener_{Guid.NewGuid():N}";
        _wndProcDelegate = WndProc;
    }

    public bool Start()
    {
        if (_isRunning) return true;

        try
        {
            _isRunning = true;
            _messageThread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "ClipboardListener"
            };
            _messageThread.SetApartmentState(ApartmentState.STA);
            _messageThread.Start();

            // Wait for window creation
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (_hwnd == IntPtr.Zero && DateTime.UtcNow < timeout && _isRunning)
            {
                Thread.Sleep(10);
            }

            if (_hwnd == IntPtr.Zero)
            {
                _isRunning = false;
                Log.Warning("Failed to create clipboard listener window");
                return false;
            }

            Log.Information("Windows clipboard listener started");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Windows clipboard listener");
            _isRunning = false;
            return false;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;

        if (_hwnd != IntPtr.Zero)
        {
            PostMessageW(_hwnd, WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
        }

        _messageThread?.Join(1000);
        Log.Information("Windows clipboard listener stopped");
    }

    private void MessageLoop()
    {
        try
        {
            var hInstance = GetModuleHandleW(IntPtr.Zero);

            var wndClass = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = hInstance,
                lpszClassName = _className
            };

            var classAtom = RegisterClassW(ref wndClass);
            if (classAtom == 0)
            {
                Log.Error("Failed to register window class: {Error}", Marshal.GetLastWin32Error());
                return;
            }

            try
            {
                _hwnd = CreateWindowExW(
                    0,
                    _className,
                    "Krypton Clipboard Listener",
                    0,
                    0, 0, 0, 0,
                    new IntPtr(HWND_MESSAGE),
                    IntPtr.Zero,
                    hInstance,
                    IntPtr.Zero);

                if (_hwnd == IntPtr.Zero)
                {
                    Log.Error("Failed to create window: {Error}", Marshal.GetLastWin32Error());
                    return;
                }

                if (!AddClipboardFormatListener(_hwnd))
                {
                    Log.Error("Failed to add clipboard format listener: {Error}", Marshal.GetLastWin32Error());
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                    return;
                }

                while (_isRunning && GetMessageW(out var msg, IntPtr.Zero, 0, 0))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }
            finally
            {
                if (_hwnd != IntPtr.Zero)
                {
                    RemoveClipboardFormatListener(_hwnd);
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }
                UnregisterClassW(_className, hInstance);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in clipboard listener message loop");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            try
            {
                ClipboardChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error handling clipboard update");
            }
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    ~WindowsClipboardListener()
    {
        Dispose();
    }
}
