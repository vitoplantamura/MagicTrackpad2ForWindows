using Microsoft.Win32;

namespace AmtPtpControlPanel.WinUI3
{
    public sealed class TrackpadSettings
    {
        // These defaults are only used when a value is missing from the registry, so they
        // must match the fallbacks in the drivers (PtpFilterReadSettings in
        // AmtPtpHidFilter/Driver.c and AmtPtpServiceTouchInputInterruptType5 in
        // AmtPtpDeviceUsbUm/InputInterrupt.c). Otherwise the UI shows settings that the
        // trackpad is not actually using.
        public int ButtonDisabled;
        public int FeedbackClick = 0x08081E;
        public int FeedbackRelease = 0x020218;
        public int StopPressure = -1;
        public int StopSize = -1;
        public int IgnoreButtonFinger;
        public int IgnoreNearFingers = 1;
        public int PalmRejection = 0;
        public int InvertMovement = 0;
        public int AllowNonAdminAccess = 0;
    }

    public static class SettingsService
    {
        private const string UmdfParametersPath =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters";

        private const string FilterParametersPath =
            @"SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters";

        private const string WudfServicesBase =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services";

        private const string SystemServicesBase =
            @"SYSTEM\CurrentControlSet\Services";

        private static readonly string[] ValueNames =
        {
            "ButtonDisabled",
            "FeedbackClick",
            "FeedbackRelease",
            "StopPressure",
            "StopSize",
            "IgnoreButtonFinger",
            "IgnoreNearFingers",
            "PalmRejection",
            "InvertMovement",
            "AllowNonAdminAccess",
        };

        public static TrackpadSettings Load()
        {
            TrackpadSettings s = new TrackpadSettings();

            try
            {
                // The USB driver's key is read first, as before. Installing or updating the
                // driver package recreates that key empty while the Bluetooth filter's key
                // survives, so a value missing from it is taken from the Bluetooth key.
                using (RegistryKey? usb = Registry.LocalMachine.OpenSubKey(UmdfParametersPath))
                using (RegistryKey? bt = Registry.LocalMachine.OpenSubKey(FilterParametersPath))
                {
                    int Get(string name, int fallback) => GetInt32(usb, name) ?? GetInt32(bt, name) ?? fallback;

                    s.ButtonDisabled = Get("ButtonDisabled", s.ButtonDisabled);
                    s.FeedbackClick = Get("FeedbackClick", s.FeedbackClick);
                    s.FeedbackRelease = Get("FeedbackRelease", s.FeedbackRelease);
                    s.StopPressure = Get("StopPressure", s.StopPressure);
                    s.StopSize = Get("StopSize", s.StopSize);
                    s.IgnoreButtonFinger = Get("IgnoreButtonFinger", s.IgnoreButtonFinger);
                    s.IgnoreNearFingers = Get("IgnoreNearFingers", s.IgnoreNearFingers);
                    s.PalmRejection = Get("PalmRejection", s.PalmRejection);
                    s.InvertMovement = Get("InvertMovement", s.InvertMovement);
                    s.AllowNonAdminAccess = Get("AllowNonAdminAccess", s.AllowNonAdminAccess);
                }
            }
            catch
            {
                // Fall back to defaults if the driver parameters cannot be read.
            }

            return s;
        }

        public static void Save(TrackpadSettings s)
        {
            Save(WudfServicesBase, "AmtPtpDeviceUsbUm", s);
            Save(SystemServicesBase, "AmtPtpHidFilter", s);
        }

        /// <summary>
        /// Copies every value that exists in only one of the two drivers' Parameters keys
        /// into the other one, so that USB and Bluetooth use the same settings again after
        /// a driver install has emptied the USB key. Values present in both keys are left
        /// alone. Returns true if anything was written, so the caller can ask the drivers
        /// to reload their settings.
        /// </summary>
        public static bool SyncMissingValues()
        {
            bool changed = false;

            try
            {
                using (RegistryKey? usb = OpenParametersForWriting(WudfServicesBase + @"\AmtPtpDeviceUsbUm"))
                using (RegistryKey? bt = OpenParametersForWriting(SystemServicesBase + @"\AmtPtpHidFilter"))
                {
                    // Nothing to sync unless both drivers are installed.
                    if (usb == null || bt == null)
                        return false;

                    foreach (string name in ValueNames)
                    {
                        int? usbValue = GetInt32(usb, name);
                        int? btValue = GetInt32(bt, name);

                        if (usbValue == null && btValue != null)
                        {
                            usb.SetValue(name, btValue.Value);
                            changed = true;
                        }
                        else if (btValue == null && usbValue != null)
                        {
                            bt.SetValue(name, usbValue.Value);
                            changed = true;
                        }
                    }
                }
            }
            catch
            {
                // Best effort: pressing Apply writes both keys anyway.
            }

            return changed;
        }

        private static RegistryKey? OpenParametersForWriting(string serviceKeyPath)
        {
            using (RegistryKey? service = Registry.LocalMachine.OpenSubKey(serviceKeyPath, true))
            {
                return service?.CreateSubKey("Parameters", true);
            }
        }

        private static int? GetInt32(RegistryKey? key, string name)
        {
            try
            {
                if (key?.GetValue(name) is int i)
                    return i;
            }
            catch
            {
            }

            return null;
        }

        private static void Save(string baseKeyPath, string serviceName, TrackpadSettings s)
        {
            using (RegistryKey? services = Registry.LocalMachine.OpenSubKey(baseKeyPath, true))
            {
                if (services == null)
                    throw new System.Exception($"Unable to open registry key '{baseKeyPath}' for writing.");

                using (RegistryKey device = services.CreateSubKey(serviceName, true))
                using (RegistryKey parameters = device.CreateSubKey("Parameters", true))
                {
                    parameters.SetValue("ButtonDisabled", s.ButtonDisabled);
                    parameters.SetValue("FeedbackClick", s.FeedbackClick);
                    parameters.SetValue("FeedbackRelease", s.FeedbackRelease);
                    parameters.SetValue("StopPressure", s.StopPressure);
                    parameters.SetValue("StopSize", s.StopSize);
                    parameters.SetValue("IgnoreButtonFinger", s.IgnoreButtonFinger);
                    parameters.SetValue("IgnoreNearFingers", s.IgnoreNearFingers);
                    parameters.SetValue("PalmRejection", s.PalmRejection);
                    parameters.SetValue("InvertMovement", s.InvertMovement);
                    parameters.SetValue("AllowNonAdminAccess", s.AllowNonAdminAccess);
                }
            }
        }
    }
}
