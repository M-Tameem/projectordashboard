using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProjectorDash
{
    public sealed class AudioDeviceInfo
    {
        public string Id;
        public string Name;
        public bool IsDefault;

        public override string ToString()
        {
            return Name + (IsDefault ? "  (current)" : "");
        }
    }

    /// <summary>
    /// Core Audio endpoint enumeration, selection, per-device master volume,
    /// and temporary per-session muting for the alarm. Windows stores volume
    /// and mute independently on each endpoint, so switching devices never
    /// copies the HDMI/tablet/headphone state over another device.
    /// </summary>
    public sealed class AudioEndpoint
    {
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport]
        [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        private class PolicyConfigClientComObject { }

        private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
        private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask,
                out IntPtr devices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role,
                out IMMDevice endpoint);
            [PreserveSig]
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id,
                out IMMDevice device);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-C0A2BBA099B4")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig]
            int GetCount(out uint count);
            [PreserveSig]
            int Item(uint index, out IMMDevice device);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CollectionGetCount(IntPtr self, out uint count);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CollectionItem(IntPtr self, uint index, out IntPtr device);

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig]
            int OpenPropertyStore(int access, out IPropertyStore properties);
            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig]
            int GetState(out int state);
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint count);
            [PreserveSig]
            int GetAt(uint index, out PropertyKey key);
            [PreserveSig]
            int GetValue(ref PropertyKey key, out PropVariant value);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IAudioEndpointVolume
        {
            [PreserveSig]
            int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig]
            int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig]
            int GetChannelCount(out uint count);
            [PreserveSig]
            int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
            [PreserveSig]
            int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            [PreserveSig]
            int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig]
            int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
            [PreserveSig]
            int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
            [PreserveSig]
            int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig]
            int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
            [PreserveSig]
            int GetMute(out bool mute);
        }

        // This interface is present on Windows 7/8.1 and is what the Sound
        // control panel uses to assign a preferred default endpoint.
        [ComImport]
        [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig]
            int GetMixFormat(string deviceId, IntPtr format);
            [PreserveSig]
            int GetDeviceFormat(string deviceId, int isDefault, IntPtr format);
            [PreserveSig]
            int ResetDeviceFormat(string deviceId);
            [PreserveSig]
            int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig]
            int GetProcessingPeriod(string deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
            [PreserveSig]
            int SetProcessingPeriod(string deviceId, IntPtr period);
            [PreserveSig]
            int GetShareMode(string deviceId, IntPtr mode);
            [PreserveSig]
            int SetShareMode(string deviceId, IntPtr mode);
            [PreserveSig]
            int GetPropertyValue(string deviceId, IntPtr key, IntPtr value);
            [PreserveSig]
            int SetPropertyValue(string deviceId, IntPtr key, IntPtr value);
            [PreserveSig]
            int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
            [PreserveSig]
            int SetEndpointVisibility(string deviceId, int visible);
        }

        [ComImport]
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig]
            int GetAudioSessionControl(ref Guid sessionGuid, uint streamFlags,
                out IAudioSessionControl sessionControl);
            [PreserveSig]
            int GetSimpleAudioVolume(ref Guid sessionGuid, uint streamFlags,
                out ISimpleAudioVolume volume);
            [PreserveSig]
            int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
        }

        [ComImport]
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig]
            int GetCount(out int count);
            [PreserveSig]
            int GetSession(int index, out IAudioSessionControl control);
        }

        [ComImport]
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            [PreserveSig]
            int GetState(out int state);
            [PreserveSig]
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig]
            int SetDisplayName(string value, ref Guid eventContext);
            [PreserveSig]
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig]
            int SetIconPath(string value, ref Guid eventContext);
            [PreserveSig]
            int GetGroupingParam(out Guid groupingId);
            [PreserveSig]
            int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
            [PreserveSig]
            int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig]
            int UnregisterAudioSessionNotification(IntPtr client);
        }

        [ComImport]
        [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl methods
            [PreserveSig]
            int GetState(out int state);
            [PreserveSig]
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig]
            int SetDisplayName(string value, ref Guid eventContext);
            [PreserveSig]
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig]
            int SetIconPath(string value, ref Guid eventContext);
            [PreserveSig]
            int GetGroupingParam(out Guid groupingId);
            [PreserveSig]
            int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
            [PreserveSig]
            int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig]
            int UnregisterAudioSessionNotification(IntPtr client);
            // IAudioSessionControl2 methods
            [PreserveSig]
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
            [PreserveSig]
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
            [PreserveSig]
            int GetProcessId(out uint processId);
            [PreserveSig]
            int IsSystemSoundsSession();
            [PreserveSig]
            int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport]
        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface ISimpleAudioVolume
        {
            [PreserveSig]
            int SetMasterVolume(float level, ref Guid eventContext);
            [PreserveSig]
            int GetMasterVolume(out float level);
            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
            [PreserveSig]
            int GetMute(out bool mute);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;
        }

        // PROPVARIANT is 16 bytes in x86 and 24 in x64. Reserving 24 is safe
        // for the x86 target and also keeps reflection smoke tests safe on x64.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort VariantType;
            [FieldOffset(8)] public IntPtr PointerValue;
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        internal sealed class SessionMuteState
        {
            public string InstanceId;
            public ISimpleAudioVolume Volume;
            public bool WasMuted;
            public float WasVolume;
            public bool IsAlarmSession;
        }

        internal sealed class AlarmAudioState
        {
            internal string _previousDefaultId;
            internal string _alarmDeviceId;
            internal float _previousVolume;
            internal bool _previousMute;
            internal IAudioEndpointVolume _alarmVolume;
            internal readonly List<SessionMuteState> _sessions =
                new List<SessionMuteState>();
            internal readonly HashSet<string> _sessionIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private const int DeviceStateActive = 0x00000001;
        private const int StgmRead = 0;
        private const int ClsCtxInprocServer = 1;
        private static readonly PropertyKey FriendlyNameKey = new PropertyKey
        {
            FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
            PropertyId = 14
        };

        private IMMDeviceEnumerator _enumerator;
        private IAudioEndpointVolume _volume;
        private string _currentDeviceId = "";
        private string _currentDeviceName = "";
        private string _diagnostic = "";
        private Guid _empty = Guid.Empty;

        public bool Available { get { return _volume != null; } }
        public string CurrentDeviceId { get { return _currentDeviceId; } }
        public string CurrentDeviceName { get { return _currentDeviceName; } }
        public string Diagnostic { get { return _diagnostic; } }

        public AudioEndpoint(string preferredDeviceId)
        {
            try
            {
                _enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                if (!string.IsNullOrEmpty(preferredDeviceId) && IsDeviceActive(preferredDeviceId))
                    ActivateDevice(preferredDeviceId);
                else
                    ActivateDefaultDevice();
            }
            catch
            {
                _volume = null;
            }
        }

        public List<AudioDeviceInfo> GetDevices()
        {
            List<AudioDeviceInfo> list = new List<AudioDeviceInfo>();
            if (_enumerator == null) return list;
            string defaultId = GetDefaultDeviceId();
            try
            {
                IntPtr collection;
                int enumResult = _enumerator.EnumAudioEndpoints(EDataFlow.eRender,
                    DeviceStateActive, out collection);
                _diagnostic = "EnumAudioEndpoints=" + enumResult.ToString();
                if (enumResult != 0 || collection == IntPtr.Zero) return list;
                uint count;
                IntPtr vtable = Marshal.ReadIntPtr(collection);
                CollectionGetCount getCount = (CollectionGetCount)
                    Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable,
                        IntPtr.Size * 3), typeof(CollectionGetCount));
                CollectionItem getItem = (CollectionItem)
                    Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable,
                        IntPtr.Size * 4), typeof(CollectionItem));
                int countResult = getCount(collection, out count);
                _diagnostic += ", GetCount=" + countResult.ToString() +
                    ", Count=" + count.ToString();
                try
                {
                    for (uint i = 0; i < count; i++)
                    {
                        IntPtr devicePointer;
                        if (getItem(collection, i, out devicePointer) != 0 ||
                            devicePointer == IntPtr.Zero) continue;
                        try
                        {
                            object deviceObject = Marshal.GetObjectForIUnknown(devicePointer);
                            IMMDevice device = (IMMDevice)deviceObject;
                            string id;
                            device.GetId(out id);
                            list.Add(new AudioDeviceInfo
                            {
                                Id = id,
                                Name = GetFriendlyName(device),
                                IsDefault = string.Equals(id, defaultId,
                                    StringComparison.OrdinalIgnoreCase)
                            });
                        }
                        finally { Marshal.Release(devicePointer); }
                    }
                }
                finally { Marshal.Release(collection); }
            }
            catch (Exception ex) { _diagnostic = ex.GetType().Name + ": " + ex.Message; }
            return list;
        }

        public string FindLikelyInternalSpeakers()
        {
            int bestScore = -1000;
            string bestId = "";
            foreach (AudioDeviceInfo device in GetDevices())
            {
                int score = TabletSpeakerScore(device.Name);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = device.Id;
                }
            }
            return bestScore >= 130 ? bestId : "";
        }

        public bool IsSafeTabletSpeaker(string deviceId)
        {
            foreach (AudioDeviceInfo device in GetDevices())
            {
                if (string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                    return TabletSpeakerScore(device.Name) >= 130;
            }
            return false;
        }

        public bool SelectDevice(string deviceId)
        {
            if (!IsDeviceActive(deviceId)) return false;
            if (!SetDefaultMediaDevice(deviceId)) return false;
            return ActivateDevice(deviceId);
        }

        /// <summary>
        /// Keeps a persisted preference across disconnect/reconnect. While the
        /// preferred device is absent, controls follow Windows' fallback
        /// default without changing the saved preference.
        /// </summary>
        public bool RefreshForPreferredDevice(string preferredDeviceId)
        {
            string wanted = "";
            if (!string.IsNullOrEmpty(preferredDeviceId) && IsDeviceActive(preferredDeviceId))
                wanted = preferredDeviceId;
            else
                wanted = GetDefaultDeviceId();
            if (string.IsNullOrEmpty(wanted) || string.Equals(wanted,
                _currentDeviceId, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(preferredDeviceId) &&
                string.Equals(wanted, preferredDeviceId, StringComparison.OrdinalIgnoreCase))
                SetDefaultMediaDevice(wanted);
            return ActivateDevice(wanted);
        }

        public int GetVolume()
        {
            if (_volume == null) return 0;
            try
            {
                float level;
                _volume.GetMasterVolumeLevelScalar(out level);
                return (int)Math.Round(level * 100.0f);
            }
            catch { return 0; }
        }

        public void SetVolume(int percent)
        {
            if (_volume == null) return;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            try { _volume.SetMasterVolumeLevelScalar(percent / 100.0f, ref _empty); }
            catch { }
        }

        public bool GetMute()
        {
            if (_volume == null) return false;
            try
            {
                bool muted;
                _volume.GetMute(out muted);
                return muted;
            }
            catch { return false; }
        }

        public void SetMute(bool mute)
        {
            if (_volume == null) return;
            try { _volume.SetMute(mute, ref _empty); }
            catch { }
        }

        internal AlarmAudioState BeginAlarm(string tabletSpeakerDeviceId,
            int dashboardProcessId)
        {
            if (!IsDeviceActive(tabletSpeakerDeviceId)) return null;
            try
            {
                IMMDevice alarmDevice;
                if (_enumerator.GetDevice(tabletSpeakerDeviceId, out alarmDevice) != 0)
                    return null;
                IAudioEndpointVolume alarmVolume = ActivateVolume(alarmDevice);
                if (alarmVolume == null) return null;

                AlarmAudioState state = new AlarmAudioState();
                state._previousDefaultId = GetDefaultDeviceId();
                state._alarmDeviceId = tabletSpeakerDeviceId;
                state._alarmVolume = alarmVolume;
                alarmVolume.GetMasterVolumeLevelScalar(out state._previousVolume);
                alarmVolume.GetMute(out state._previousMute);

                // Mute app sessions before changing the default so existing
                // media cannot follow the switch and leak through the tablet.
                MuteOtherSessions(state, dashboardProcessId);
                if (!SetDefaultMediaDevice(tabletSpeakerDeviceId))
                {
                    RestoreSessions(state);
                    return null;
                }
                PrepareAlarmPlayback(state, dashboardProcessId);
                return state;
            }
            catch { return null; }
        }

        internal void PrepareAlarmPlayback(AlarmAudioState state, int dashboardProcessId)
        {
            if (state == null) return;
            try
            {
                SetDefaultMediaDevice(state._alarmDeviceId);
                state._alarmVolume.SetMasterVolumeLevelScalar(1.0f, ref _empty);
                state._alarmVolume.SetMute(false, ref _empty);
                foreach (SessionMuteState session in state._sessions)
                {
                    if (session.IsAlarmSession)
                    {
                        session.Volume.SetMasterVolume(1.0f, ref _empty);
                        session.Volume.SetMute(false, ref _empty);
                    }
                    else
                    {
                        session.Volume.SetMute(true, ref _empty);
                    }
                }
                MuteOtherSessions(state, dashboardProcessId);
            }
            catch { }
        }

        internal void EndAlarm(AlarmAudioState state, string preferredDeviceId)
        {
            if (state == null) return;
            RestoreSessions(state);
            try
            {
                state._alarmVolume.SetMasterVolumeLevelScalar(
                    state._previousVolume, ref _empty);
                state._alarmVolume.SetMute(state._previousMute, ref _empty);
            }
            catch { }

            string restore = state._previousDefaultId;
            if (!IsDeviceActive(restore) && IsDeviceActive(preferredDeviceId))
                restore = preferredDeviceId;
            if (IsDeviceActive(restore)) SetDefaultMediaDevice(restore);
            RefreshForPreferredDevice(preferredDeviceId);
        }

        private void MuteOtherSessions(AlarmAudioState state, int dashboardProcessId)
        {
            foreach (AudioDeviceInfo info in GetDevices())
            {
                try
                {
                    IMMDevice device;
                    if (_enumerator.GetDevice(info.Id, out device) != 0) continue;
                    Guid iid = typeof(IAudioSessionManager2).GUID;
                    object instance;
                    if (device.Activate(ref iid, ClsCtxInprocServer,
                        IntPtr.Zero, out instance) != 0) continue;
                    IAudioSessionManager2 manager = (IAudioSessionManager2)instance;
                    IAudioSessionEnumerator sessions;
                    if (manager.GetSessionEnumerator(out sessions) != 0 || sessions == null)
                        continue;
                    int count;
                    sessions.GetCount(out count);
                    for (int i = 0; i < count; i++)
                    {
                        IAudioSessionControl control;
                        if (sessions.GetSession(i, out control) != 0 || control == null)
                            continue;
                        IAudioSessionControl2 control2 = control as IAudioSessionControl2;
                        ISimpleAudioVolume simple = control as ISimpleAudioVolume;
                        if (control2 == null || simple == null) continue;
                        uint processId;
                        control2.GetProcessId(out processId);
                        bool isAlarmSession = (int)processId == dashboardProcessId ||
                            control2.IsSystemSoundsSession() == 0;
                        string instanceId;
                        control2.GetSessionInstanceIdentifier(out instanceId);
                        if (string.IsNullOrEmpty(instanceId))
                            instanceId = info.Id + "|" + processId.ToString() + "|" + i.ToString();
                        if (state._sessionIds.Contains(instanceId)) continue;
                        bool muted;
                        float volume;
                        simple.GetMute(out muted);
                        simple.GetMasterVolume(out volume);
                        state._sessionIds.Add(instanceId);
                        SessionMuteState saved = new SessionMuteState
                        {
                            InstanceId = instanceId,
                            Volume = simple,
                            WasMuted = muted,
                            WasVolume = volume,
                            IsAlarmSession = isAlarmSession
                        };
                        state._sessions.Add(saved);
                        if (isAlarmSession)
                        {
                            simple.SetMasterVolume(1.0f, ref _empty);
                            simple.SetMute(false, ref _empty);
                        }
                        else simple.SetMute(true, ref _empty);
                    }
                }
                catch { }
            }
        }

        private void RestoreSessions(AlarmAudioState state)
        {
            foreach (SessionMuteState session in state._sessions)
            {
                try
                {
                    if (session.IsAlarmSession)
                        session.Volume.SetMasterVolume(session.WasVolume, ref _empty);
                    session.Volume.SetMute(session.WasMuted, ref _empty);
                }
                catch { }
            }
            state._sessions.Clear();
            state._sessionIds.Clear();
        }

        private bool ActivateDefaultDevice()
        {
            string id = GetDefaultDeviceId();
            return !string.IsNullOrEmpty(id) && ActivateDevice(id);
        }

        private bool ActivateDevice(string deviceId)
        {
            try
            {
                IMMDevice device;
                if (_enumerator.GetDevice(deviceId, out device) != 0 || device == null)
                    return false;
                IAudioEndpointVolume volume = ActivateVolume(device);
                if (volume == null) return false;
                _volume = volume;
                _currentDeviceId = deviceId;
                _currentDeviceName = GetFriendlyName(device);
                return true;
            }
            catch { return false; }
        }

        private static IAudioEndpointVolume ActivateVolume(IMMDevice device)
        {
            try
            {
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object instance;
                if (device.Activate(ref iid, ClsCtxInprocServer,
                    IntPtr.Zero, out instance) != 0) return null;
                return (IAudioEndpointVolume)instance;
            }
            catch { return null; }
        }

        private bool IsDeviceActive(string deviceId)
        {
            if (_enumerator == null || string.IsNullOrEmpty(deviceId)) return false;
            try
            {
                IMMDevice device;
                if (_enumerator.GetDevice(deviceId, out device) != 0 || device == null)
                    return false;
                int state;
                device.GetState(out state);
                return (state & DeviceStateActive) != 0;
            }
            catch { return false; }
        }

        private string GetDefaultDeviceId()
        {
            if (_enumerator == null) return "";
            try
            {
                IMMDevice device;
                if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender,
                    ERole.eConsole, out device) != 0 || device == null) return "";
                string id;
                device.GetId(out id);
                return id ?? "";
            }
            catch { return ""; }
        }

        private static string GetFriendlyName(IMMDevice device)
        {
            try
            {
                IPropertyStore store;
                if (device.OpenPropertyStore(StgmRead, out store) != 0 || store == null)
                    return "Audio device";
                PropertyKey key = FriendlyNameKey;
                PropVariant value;
                if (store.GetValue(ref key, out value) != 0) return "Audio device";
                try
                {
                    if (value.VariantType == 31 && value.PointerValue != IntPtr.Zero)
                        return Marshal.PtrToStringUni(value.PointerValue) ?? "Audio device";
                }
                finally { PropVariantClear(ref value); }
            }
            catch { }
            return "Audio device";
        }

        private static bool SetDefaultMediaDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            try
            {
                IPolicyConfig policy = (IPolicyConfig)(new PolicyConfigClientComObject());
                bool ok = true;
                // Never assign the selected media device to the Communications
                // role. Doing that to Bluetooth headphones can route an
                // existing call/communications stream onto their HFP endpoint,
                // which drops playback into low-bandwidth headset mode even
                // though the dashboard itself never opens a microphone.
                ok &= policy.SetDefaultEndpoint(deviceId, ERole.eConsole) == 0;
                ok &= policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia) == 0;
                return ok;
            }
            catch { return false; }
        }

        private static int TabletSpeakerScore(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            int score = 0;
            if (n.Contains("speaker")) score += 100;
            if (n.Contains("internal") || n.Contains("realtek") ||
                n.Contains("intel") || n.Contains("sst")) score += 30;
            if (n.Contains("bluetooth") || n.Contains(" bt ") ||
                n.Contains("headphone") || n.Contains("headset") ||
                n.Contains("hdmi") || n.Contains("display audio") ||
                n.Contains("projector") || n.Contains("television") ||
                n.Contains(" usb")) score -= 300;
            return score;
        }
    }
}
