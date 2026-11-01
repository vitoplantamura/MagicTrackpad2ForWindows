using Microsoft.Win32;

namespace AmtPtpControlPanel.WinUI3
{
    public sealed class TrackpadSettings
    {
        public int ButtonDisabled;
        public int FeedbackClick = 0x060617;
        public int FeedbackRelease = 0x000014;
        public int StopPressure = -1;
        public int StopSize = -1;
        public int IgnoreButtonFinger;
        public int IgnoreNearFingers = 1;
        public int PalmRejection = 1;
        public int InvertMovement = 0;
        public int AllowNonAdminAccess = 0;
    }

    public static class SettingsService
    {
        private const string UmdfParametersPath =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters";

        private const string WudfServicesBase =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services";

        private const string SystemServicesBase =
            @"SYSTEM\CurrentControlSet\Services";

        public static TrackpadSettings Load()
        {
            TrackpadSettings s = new TrackpadSettings();

            try
            {
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(UmdfParametersPath))
                {
                    if (key == null)
                        return s;

                    s.ButtonDisabled = GetInt32(key, "ButtonDisabled", s.ButtonDisabled);
                    s.FeedbackClick = GetInt32(key, "FeedbackClick", s.FeedbackClick);
                    s.FeedbackRelease = GetInt32(key, "FeedbackRelease", s.FeedbackRelease);
                    s.StopPressure = GetInt32(key, "StopPressure", s.StopPressure);
                    s.StopSize = GetInt32(key, "StopSize", s.StopSize);
                    s.IgnoreButtonFinger = GetInt32(key, "IgnoreButtonFinger", s.IgnoreButtonFinger);
                    s.IgnoreNearFingers = GetInt32(key, "IgnoreNearFingers", s.IgnoreNearFingers);
                    s.PalmRejection = GetInt32(key, "PalmRejection", s.PalmRejection);
                    s.InvertMovement = GetInt32(key, "InvertMovement", s.InvertMovement);
                    s.AllowNonAdminAccess = GetInt32(key, "AllowNonAdminAccess", s.AllowNonAdminAccess);
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

        private static int GetInt32(RegistryKey key, string name, int fallback)
        {
            try
            {
                object? value = key.GetValue(name);
                if (value is int i)
                    return i;
            }
            catch
            {
            }

            return fallback;
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
