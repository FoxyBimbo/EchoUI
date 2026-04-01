using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrismPane_Widgets.Services;

public static class MediaPlaybackMonitor
{
    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "brave",
        "opera",
        "firefox",
        "vivaldi",
        "waterfox",
        "librewolf"
    };

    private const float PeakThreshold = 0.0001f;

    public static MediaPlaybackSnapshot GetSnapshot()
    {
        // Try the multimedia endpoint first (used by browsers and media apps),
        // then fall back to console endpoint.
        var snapshot = GetSnapshotFromEndpoint(ERole.eMultimedia);
        if (snapshot.IsPlaying)
            return snapshot;

        var consoleSnapshot = GetSnapshotFromEndpoint(ERole.eConsole);
        return consoleSnapshot.IsPlaying ? consoleSnapshot : snapshot;
    }

    private static MediaPlaybackSnapshot GetSnapshotFromEndpoint(ERole role)
    {
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        IAudioMeterInformation? audioMeter = null;

        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, role, out device);

            float peakValue = 0;
            try
            {
                var meterGuid = typeof(IAudioMeterInformation).GUID;
                device.Activate(ref meterGuid, ClsCtxAll, IntPtr.Zero, out var meterObject);
                audioMeter = (IAudioMeterInformation)meterObject;
                audioMeter.GetPeakValue(out peakValue);
            }
            catch
            {
                // Device-level meter unavailable; continue with session enumeration.
            }

            var sessionManagerGuid = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref sessionManagerGuid, ClsCtxAll, IntPtr.Zero, out var sessionManagerObject);
            sessionManager = (IAudioSessionManager2)sessionManagerObject;
            sessionManager.GetSessionEnumerator(out sessionEnumerator);
            sessionEnumerator.GetCount(out var sessionCount);

            string? bestDisplayName = null;
            string? browserDisplayName = null;
            var hasActiveSession = false;

            for (var i = 0; i < sessionCount; i++)
            {
                IAudioSessionControl? sessionControl = null;
                try
                {
                    sessionEnumerator.GetSession(i, out sessionControl);

                    var state = AudioSessionState.Inactive;
                    try { sessionControl.GetState(out state); } catch { }

                    var sessionPeak = GetSessionPeakValue(sessionControl);
                    var isActive = state == AudioSessionState.Active || sessionPeak > PeakThreshold;

                    // Resolve the process behind this session.
                    ResolveSessionProcess(sessionControl, out var resolvedName, out var processName);

                    // Even if this session isn't "active" by state, if we recognize
                    // it as a browser we still want to know about it.
                    if (!isActive && !IsBrowserProcess(processName))
                        continue;

                    hasActiveSession = true;

                    if (!string.IsNullOrWhiteSpace(resolvedName))
                        bestDisplayName ??= resolvedName;

                    if (IsBrowserProcess(processName) && !string.IsNullOrWhiteSpace(resolvedName))
                        browserDisplayName ??= resolvedName;
                }
                catch
                {
                    // Skip individual sessions that throw; don't abort the loop.
                }
                finally
                {
                    ReleaseComObject(sessionControl);
                }
            }

            // If device peak is above threshold the device is producing audio,
            // even if we couldn't attribute it to a specific session.
            var isPlaying = hasActiveSession || peakValue > PeakThreshold;
            var display = browserDisplayName ?? bestDisplayName;

            return new MediaPlaybackSnapshot(isPlaying, display, peakValue);
        }
        catch
        {
            return MediaPlaybackSnapshot.Unavailable;
        }
        finally
        {
            ReleaseComObject(audioMeter);
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
            ReleaseComObject(device);
            ReleaseComObject(deviceEnumerator);
        }
    }

    private static bool IsBrowserProcess(string? processName)
    {
        return !string.IsNullOrWhiteSpace(processName) && BrowserProcessNames.Contains(processName!);
    }

    private static void ResolveSessionProcess(IAudioSessionControl sessionControl, out string? displayName, out string? processName)
    {
        displayName = null;
        processName = null;

        // 1. Try the session display name set by the app.
        try
        {
            sessionControl.GetDisplayName(out var dn);
            if (!string.IsNullOrWhiteSpace(dn))
                displayName = dn;
        }
        catch { }

        // 2. Try process-based identification via IAudioSessionControl2.
        if (sessionControl is not IAudioSessionControl2 sc2)
            return;

        uint processId = 0;
        try { sc2.GetProcessId(out processId); } catch { }
        if (processId == 0 || processId == Environment.ProcessId)
            return;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;

            // For browser child/renderer processes, walk to the parent to get
            // the real browser name and window title.
            if (string.IsNullOrWhiteSpace(process.MainWindowTitle))
            {
                var parentName = GetParentProcessName(process);
                if (!string.IsNullOrWhiteSpace(parentName) && IsBrowserProcess(parentName))
                    processName = parentName;
            }

            // Use the best display name we can find.
            if (displayName is null)
            {
                if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    displayName = process.MainWindowTitle;
                else
                    displayName = FriendlyBrowserName(processName) ?? processName;
            }
        }
        catch { }
    }

    private static string? GetParentProcessName(Process child)
    {
        try
        {
            var handle = child.Handle;
            var pbi = new PROCESS_BASIC_INFORMATION();
            var status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out _);
            if (status != 0)
                return null;

            using var parent = Process.GetProcessById((int)pbi.InheritedFromUniqueProcessId);
            return parent.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? FriendlyBrowserName(string? processName) => processName?.ToLowerInvariant() switch
    {
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "firefox" => "Mozilla Firefox",
        "brave" => "Brave",
        "opera" => "Opera",
        "vivaldi" => "Vivaldi",
        _ => null
    };

    private static float GetSessionPeakValue(IAudioSessionControl sessionControl)
    {
        try
        {
            if (sessionControl is IAudioMeterInformation meter)
            {
                meter.GetPeakValue(out var peakValue);
                return peakValue;
            }
        }
        catch { }

        return 0;
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is null || !Marshal.IsComObject(instance))
            return;

        Marshal.ReleaseComObject(instance);
    }

    private const uint ClsCtxAll = 23;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    public readonly record struct MediaPlaybackSnapshot(bool IsPlaying, string? DisplayName, float PeakValue)
    {
        public static MediaPlaybackSnapshot Unavailable => new(false, null, 0);
    }

    private enum EDataFlow
    {
        eRender,
        eCapture,
        eAll,
        EDataFlow_enum_count
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications,
        ERole_enum_count
    }

    private enum AudioSessionState
    {
        Inactive = 0,
        Active = 1,
        Expired = 2
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out object devices);
        void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IntPtr client);
        void UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, uint dwClsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        void OpenPropertyStore(uint stgmAccess, out IntPtr properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out uint state);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        void GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IntPtr sessionControl);
        void GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out IntPtr audioVolume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        void RegisterSessionNotification(IntPtr sessionNotification);
        void UnregisterSessionNotification(IntPtr sessionNotification);
        void RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
        void UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        void GetCount(out int sessionCount);
        void GetSession(int sessionIndex, out IAudioSessionControl sessionControl);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        void GetState(out AudioSessionState state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        void GetGroupingParam(out Guid groupingId);
        void SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        void RegisterAudioSessionNotification(IntPtr client);
        void UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport]
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2 : IAudioSessionControl
    {
        new void GetState(out AudioSessionState state);
        new void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        new void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        new void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        new void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        new void GetGroupingParam(out Guid groupingId);
        new void SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        new void RegisterAudioSessionNotification(IntPtr client);
        new void UnregisterAudioSessionNotification(IntPtr client);
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionIdentifier);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceIdentifier);
        void GetProcessId(out uint processId);
        void IsSystemSoundsSession();
        void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        void GetPeakValue(out float peakValue);
        void GetMeteringChannelCount(out int channelCount);
        void GetChannelsPeakValues(int channelCount, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peakValues);
        void QueryHardwareSupport(out uint hardwareSupportMask);
    }
}
