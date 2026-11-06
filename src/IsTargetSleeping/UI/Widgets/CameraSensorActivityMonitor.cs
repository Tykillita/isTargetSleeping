using System.Runtime.InteropServices;
using System.Text;

namespace IsTargetSleeping.UI.Widgets;

/// <summary>Reads Windows' camera stream reports without opening a camera or requesting frames.</summary>
internal sealed class CameraSensorActivityMonitor : IDisposable
{
    private const uint MfVersion = 0x00020070;
    private const int MfStartupNoSocket = 0x1;
    private readonly IMFSensorActivityMonitor monitor;
    private readonly ActivityCallback callback;
    private bool disposed;

    private CameraSensorActivityMonitor(IMFSensorActivityMonitor monitor, ActivityCallback callback)
        => (this.monitor, this.callback) = (monitor, callback);

    public static CameraSensorActivityMonitor? TryStart(Action<bool, int[]> onChanged)
    {
        bool mfStarted = false;
        IMFSensorActivityMonitor? monitor = null;
        try
        {
            Marshal.ThrowExceptionForHR(MFStartup(MfVersion, MfStartupNoSocket));
            mfStarted = true;
            var callback = new ActivityCallback(onChanged);
            Marshal.ThrowExceptionForHR(MFCreateSensorActivityMonitor(callback, out monitor));
            Marshal.ThrowExceptionForHR(monitor.Start());
            return new CameraSensorActivityMonitor(monitor, callback);
        }
        catch (Exception e)
        {
            if (monitor is not null)
            {
                try { monitor.Stop(); } catch { }
                Shutdown(monitor);
                Marshal.ReleaseComObject(monitor);
            }
            if (mfStarted) MFShutdown();
            AppLog.Write($"Cámara: monitor de actividad de Windows: {e.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { monitor.Stop(); } catch { }
        Shutdown(monitor);
        Marshal.ReleaseComObject(monitor);
        MFShutdown();
        GC.KeepAlive(callback);
    }

    private static void Shutdown(IMFSensorActivityMonitor monitor)
    {
        try { ((IMFShutdown)monitor).Shutdown(); } catch { }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivityCallback(Action<bool, int[]> onChanged) : IMFSensorActivitiesReportCallback
    {
        private readonly object gate = new();
        private readonly Dictionary<string, (bool InUse, int[] ProcessIds)> activeDevices = new(StringComparer.OrdinalIgnoreCase);
        private int[] lastPublishedProcessIds = [];
        private bool lastPublishedInUse;
        private bool hasPublished;

        public int OnActivitiesReport(IMFSensorActivitiesReport reports)
        {
            try
            {
                Marshal.ThrowExceptionForHR(reports.GetCount(out uint count));
                if (count == 0) return 0;

                bool inUse;
                int[] processIds;
                lock (gate)
                {
                    for (uint i = 0; i < count; i++)
                    {
                        if (reports.GetActivityReport(i, out var activity) < 0 || activity is null) continue;
                        try
                        {
                            var link = new StringBuilder(1024);
                            if (activity.GetSymbolicLink(link, (uint)link.Capacity, out _) < 0) continue;
                            if (activity.GetProcessCount(out uint processes) < 0) continue;

                            bool active = false;
                            var processStates = new List<(int ProcessId, bool Streaming)>();
                            for (uint p = 0; p < processes; p++)
                            {
                                if (activity.GetProcessActivity(p, out var process) < 0 || process is null) continue;
                                try
                                {
                                    bool streaming = process.GetStreamingState(out int state) >= 0 && state != 0;
                                    if (process.GetProcessId(out uint processId) >= 0 && processId is > 0 and <= int.MaxValue)
                                        processStates.Add(((int)processId, streaming));
                                    if (streaming)
                                    {
                                        active = true;
                                    }
                                }
                                finally { Marshal.ReleaseComObject(process); }
                            }
                            activeDevices[link.ToString()] = (active, CameraUsage.ActiveProcessIds(processStates));
                        }
                        finally { Marshal.ReleaseComObject(activity); }
                    }

                    inUse = activeDevices.Values.Any(device => device.InUse);
                    processIds = activeDevices.Values.SelectMany(device => device.ProcessIds).Distinct().Order().ToArray();
                    if (hasPublished && lastPublishedInUse == inUse && lastPublishedProcessIds.SequenceEqual(processIds)) return 0;
                    hasPublished = true;
                    lastPublishedInUse = inUse;
                    lastPublishedProcessIds = processIds;
                }
                onChanged(inUse, processIds);
                return 0;
            }
            catch (Exception e) { return Marshal.GetHRForException(e); }
        }
    }

    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int MFStartup(uint version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int MFShutdown();

    [DllImport("mfsensorgroup.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int MFCreateSensorActivityMonitor(
        [MarshalAs(UnmanagedType.Interface)] IMFSensorActivitiesReportCallback callback,
        [MarshalAs(UnmanagedType.Interface)] out IMFSensorActivityMonitor monitor);

    [ComVisible(true)]
    [Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivitiesReportCallback
    {
        [PreserveSig] int OnActivitiesReport([In, MarshalAs(UnmanagedType.Interface)] IMFSensorActivitiesReport reports);
    }

    [ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivityMonitor
    {
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
    }

    [ComImport, Guid("97EC2EA4-0E42-4937-97AC-9D6D328824E1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFShutdown
    {
        [PreserveSig] int GetShutdownStatus(out int status);
        [PreserveSig] int Shutdown();
    }

    [ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivitiesReport
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetActivityReport(uint index, [MarshalAs(UnmanagedType.Interface)] out IMFSensorActivityReport activity);
    }

    [ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivityReport
    {
        [PreserveSig] int GetFriendlyName([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint capacity, out uint written);
        [PreserveSig] int GetSymbolicLink([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint capacity, out uint written);
        [PreserveSig] int GetProcessCount(out uint count);
        [PreserveSig] int GetProcessActivity(uint index, [MarshalAs(UnmanagedType.Interface)] out IMFSensorProcessActivity process);
    }

    [ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorProcessActivity
    {
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int GetStreamingState(out int streaming);
        [PreserveSig] int GetStreamingMode(out int mode);
        [PreserveSig] int GetReportTime(out long fileTime);
    }
}
