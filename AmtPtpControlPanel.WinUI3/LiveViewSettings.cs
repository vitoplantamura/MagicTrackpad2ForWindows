using Microsoft.Win32;

namespace AmtPtpControlPanel.WinUI3
{
    /// <summary>
    /// Preferences of the live view, kept next to the UI language in
    /// HKCU\Software\AmtPtpControlPanel. They only affect how the view looks.
    /// </summary>
    public static class LiveViewSettings
    {
        private const string RegistryPath = @"Software\AmtPtpControlPanel";

        public static bool DarkPad
        {
            get => Read("LiveViewDarkPad", 0) != 0;
            set => Write("LiveViewDarkPad", value ? 1 : 0);
        }

        public static bool ShowHover
        {
            get => Read("LiveViewShowHover", 1) != 0;
            set => Write("LiveViewShowHover", value ? 1 : 0);
        }

        public static bool ShowDetails
        {
            get => Read("LiveViewShowDetails", 0) != 0;
            set => Write("LiveViewShowDetails", value ? 1 : 0);
        }

        private static int Read(string name, int fallback)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key?.GetValue(name) is int value)
                        return value;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static void Write(string name, int value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath, true))
                {
                    key.SetValue(name, value);
                }
            }
            catch
            {
                // Cosmetic preference; ignore failures.
            }
        }
    }
}
