using System.Runtime.InteropServices;
using System.Text;

namespace IsTargetSleeping;

/// Todo lo que se pide directamente a Windows. Sin paquetes intermedios: la app
/// habla con el kernel, el shell, el SCM y el DWM igual que una app en C++.
public static class Win32
{
    // MARK: procesos

    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint SYNCHRONIZE = 0x00100000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(IntPtr process, out uint code);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    /// ProcessCommandLineInformation (60): la línea de comandos sin leer la memoria del proceso.
    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr buffer, int length, out int returned);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    public const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const int STARTF_USESTDHANDLES = 0x00000100;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(string? application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? directory,
        ref STARTUPINFO startup, out PROCESS_INFORMATION info);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public static readonly IntPtr PROC_THREAD_ATTRIBUTE_HANDLE_LIST = new(0x00020002);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    public static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
    public static extern bool CreateProcessEx(string? application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? directory,
        ref STARTUPINFOEX startup, out PROCESS_INFORMATION info);

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS_EX
    {
        public int cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS_EX counters, int size);

    // MARK: memoria

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
            ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateMemoryResourceNotification(int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryMemoryResourceNotification(IntPtr handle, out bool state);

    // MARK: consola (modos de terminal)

    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int pid);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    public static extern int GetFileType(IntPtr handle);

    // MARK: servicios (SCM)

    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_START = 0x0010;
    public const uint SERVICE_STOP = 0x0020;
    public const uint SERVICE_CONTROL_STOP = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode,
            dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenService(IntPtr manager, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool QueryServiceStatus(IntPtr service, out SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool StartService(IntPtr service, int argc, IntPtr argv);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ControlService(IntPtr service, uint control, out SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CloseServiceHandle(IntPtr handle);

    // MARK: ventanas

    public const int WM_CLOSE = 0x0010;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_APP = 0x8000;
    public const int NIN_SELECT = 0x0400;
    public const int NIN_KEYSELECT = 0x0401;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_SYSMENU = 0x00080000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_APPWINDOW = 0x00040000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public int dwFlags;
    }

    // MARK: DWM (esquinas redondeadas, borde y modo oscuro)

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_CAPTION_COLOR = 35;
    public const int DWMWA_TEXT_COLOR = 36;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    // MARK: bandeja del sistema

    public const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const int NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_SHOWTIP = 0x80;
    public const int NOTIFYICON_VERSION_4 = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);

    // MARK: íconos (HICON a partir de píxeles)

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr icon);

    // MARK: notificaciones (globo de la bandeja, que Windows 10/11 muestra como notificación)

    public const int NIF_INFO = 0x10;
    public const int NIIF_USER = 0x4, NIIF_LARGE_ICON = 0x20;
    public const int NIN_BALLOONUSERCLICK = 0x0405;

    // MARK: mensajes entre instancias (WM_COPYDATA)

    public const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, ref COPYDATASTRUCT data,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(int pid);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    // MARK: estado del usuario (pantalla completa exclusiva de Direct3D)

    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    // MARK: conexiones TCP con el proceso dueño (iphlpapi)

    public const int AF_INET = 2, AF_INET6 = 23;
    public const int TCP_TABLE_OWNER_PID_CONNECTIONS = 4;

    [DllImport("iphlpapi.dll")]
    public static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    // MARK: íconos de ejecutables

    public const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, int size, uint flags);
}
