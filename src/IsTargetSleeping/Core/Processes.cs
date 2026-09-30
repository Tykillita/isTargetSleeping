using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IsTargetSleeping;

/// Procesos por nombre y línea de comandos.
public static class Procs
{
    public readonly record struct Info(int Pid, string Name, string CommandLine);

    public static List<Info> ByName(params string[] names)
    {
        var list = new List<Info>();
        foreach (var name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p) list.Add(new Info(p.Id, name, CommandLine(p.Id) ?? ""));
            }
        }
        return list;
    }

    /// Argumentos sueltos, sin el ejecutable, en minúsculas.
    public static string[] Args(string commandLine)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts.Skip(1).Select(a => a.ToLowerInvariant()).ToArray();
    }

    public static string? CommandLine(int pid)
    {
        var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            Win32.NtQueryInformationProcess(h, 60, IntPtr.Zero, 0, out int needed);
            if (needed <= 0) return null;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (Win32.NtQueryInformationProcess(h, 60, buffer, needed, out _) != 0) return null;
                // UNICODE_STRING { USHORT Length; USHORT MaximumLength; PWSTR Buffer; }
                int length = (ushort)Marshal.ReadInt16(buffer);
                var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                return text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text, length / 2);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { Win32.CloseHandle(h); }
    }

    /// El ejecutable y el resto de la línea de comandos tal cual (para relanzarla igual).
    public static (string Exe, string Args) SplitCommandLine(string commandLine)
    {
        var s = commandLine.TrimStart();
        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) return (s.Trim('"'), "");
            return (s[1..end], s[(end + 1)..].Trim());
        }
        int space = s.IndexOf(' ');
        return space < 0 ? (s, "") : (s[..space], s[(space + 1)..].Trim());
    }

    /// Valor de una opción (`--port 8080`, `-a nombre`) en la línea de comandos original.
    public static string? Option(string commandLine, params string[] names)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        for (int i = 1; i < parts.Count; i++)
        {
            foreach (var name in names)
            {
                if (parts[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Count) return parts[i + 1];
                if (parts[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return parts[i][(name.Length + 1)..];
            }
        }
        return null;
    }

    /// PID del proceso que lo lanzó (ProcessBasicInformation), o null.
    public static int? ParentPid(int pid)
    {
        var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            // PROCESS_BASIC_INFORMATION: seis campos del tamaño de un puntero; el último es el padre.
            int size = IntPtr.Size * 6;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (Win32.NtQueryInformationProcess(h, 0, buffer, size, out _) != 0) return null;
                return (int)Marshal.ReadIntPtr(buffer, IntPtr.Size * 5);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { Win32.CloseHandle(h); }
    }

    /// Nombre del proceso sin «.exe», o null si ya no existe.
    public static string? Name(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; } catch { return null; }
    }

    public static string? ImagePath(int pid)
    {
        var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return Win32.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { Win32.CloseHandle(h); }
    }

    /// Segundos de CPU (usuario + núcleo) acumulados, o null si ya no existe.
    public static double? CpuSeconds(int pid)
    {
        var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!Win32.GetExitCodeProcess(h, out var code) || code != 259 /* STILL_ACTIVE */) return null;
            if (!Win32.GetProcessTimes(h, out _, out _, out long kernel, out long user)) return null;
            return (kernel + user) / 10_000_000.0;   // unidades de 100 ns
        }
        finally { Win32.CloseHandle(h); }
    }

    /// Memoria privada comprometida (la columna «Memoria» del Administrador de tareas).
    public static long? PrivateBytes(int pid)
    {
        var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var counters = new Win32.PROCESS_MEMORY_COUNTERS_EX { cb = Marshal.SizeOf<Win32.PROCESS_MEMORY_COUNTERS_EX>() };
            return Win32.GetProcessMemoryInfo(h, ref counters, counters.cb) ? (long)counters.PrivateUsage : null;
        }
        finally { Win32.CloseHandle(h); }
    }

    public static bool Alive(int pid)
    {
        var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return Win32.GetExitCodeProcess(h, out var code) && code == 259; }
        finally { Win32.CloseHandle(h); }
    }

    /// El proceso y sus descendientes (el servidor arrastra a sus runners).
    public static void KillTree(int pid)
    {
        try { using var p = Process.GetProcessById(pid); p.Kill(entireProcessTree: true); } catch { }
    }

    /// Pide cerrar sus ventanas, como «Salir» desde la bandeja. Devuelve si había alguna.
    public static bool CloseWindows(int pid)
    {
        bool any = false;
        Win32.EnumWindows((hwnd, _) =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out int owner);
            if (owner == pid) { Win32.PostMessage(hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); any = true; }
            return true;
        }, IntPtr.Zero);
        return any;
    }
}
