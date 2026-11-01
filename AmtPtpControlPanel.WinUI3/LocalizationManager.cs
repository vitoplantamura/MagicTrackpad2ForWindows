using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Win32;

namespace AmtPtpControlPanel.WinUI3
{
    /// <summary>
    /// Localization manager.
    ///
    /// Translations are embedded in the executable. The selected language is
    /// persisted in HKCU\Software\AmtPtpControlPanel ("Language" value). When no
    /// saved language exists, the language is inferred from the OS UI culture.
    /// </summary>
    public static class LocalizationManager
    {
        private const string ResourceName =
            "AmtPtpControlPanel.WinUI3.AmtPtpControlPanel.Localization.xml";

        private const string RegistryPath = @"Software\AmtPtpControlPanel";
        private const string LanguageValueName = "Language";

        private static XDocument? _document;
        private static Dictionary<string, string>? _strings;
        private static string _currentLanguage = "";

        public static bool IsActive => _strings != null;

        public static string CurrentLanguage => _currentLanguage;

        public static IReadOnlyList<string> AvailableLanguages { get; private set; } =
            new List<string>();

        public static void Initialize()
        {
            _document = null;
            _strings = null;
            _currentLanguage = "";
            AvailableLanguages = new List<string>();

            try
            {
                using (Stream? stream = typeof(LocalizationManager).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                        return;

                    _document = XDocument.Load(stream);
                }

                AvailableLanguages = _document.Root?
                    .Elements("Language")
                    .Select(e => (string?)e.Attribute("id"))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!)
                    .ToList() ?? new List<string>();

                string? saved = LoadLanguageFromRegistry();
                string language = saved != null && AvailableLanguages.Contains(saved, StringComparer.OrdinalIgnoreCase)
                    ? saved
                    : InferLanguageFromOs();

                ApplyLanguage(language);
            }
            catch
            {
                // If anything goes wrong, silently stay inactive.
                _document = null;
                _strings = null;
                _currentLanguage = "";
            }
        }

        public static string GetString(string key, string fallback = "")
        {
            if (_strings == null || key == null)
                return fallback;

            if (_strings.TryGetValue(key, out var value))
                return value;

            return fallback;
        }

        public static void SetLanguage(string languageId)
        {
            if (string.IsNullOrEmpty(languageId))
                return;

            ApplyLanguage(languageId);
            SaveLanguageToRegistry(languageId);
        }

        private static void ApplyLanguage(string languageId)
        {
            _currentLanguage = languageId;
            _strings = null;

            if (_document?.Root == null)
                return;

            XElement? language = _document.Root
                .Elements("Language")
                .FirstOrDefault(e => string.Equals(
                    (string?)e.Attribute("id"),
                    languageId,
                    StringComparison.OrdinalIgnoreCase));

            if (language == null)
                return;

            Dictionary<string, string> strings =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (XElement element in language.Elements("String"))
            {
                string? id = (string?)element.Attribute("id");
                string? value = element.Value;

                if (string.IsNullOrEmpty(id) || value == null)
                    continue;

                value = value.Trim();
                if (value.Length == 0)
                    continue;

                strings[id] = value;
            }

            if (strings.Count > 0)
                _strings = strings;
        }

        private static string InferLanguageFromOs()
        {
            foreach (string candidate in GetLanguageCandidates())
            {
                foreach (string id in AvailableLanguages)
                {
                    if (string.Equals(id, candidate, StringComparison.OrdinalIgnoreCase))
                        return id;
                }
            }

            return AvailableLanguages.FirstOrDefault() ?? "en";
        }

        private static IEnumerable<string> GetLanguageCandidates()
        {
            // Most specific ids first, then fall back to the plain two-letter
            // language code (for example "it" for "it-IT").
            List<string> candidates = new List<string>();

            try
            {
                candidates.Add(CultureInfo.CurrentUICulture.Name);
                candidates.Add(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            }
            catch
            {
            }

            try
            {
                candidates.Add(CultureInfo.InstalledUICulture.Name);
                candidates.Add(CultureInfo.InstalledUICulture.TwoLetterISOLanguageName);
            }
            catch
            {
            }

            return candidates;
        }

        private static string? LoadLanguageFromRegistry()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    return key?.GetValue(LanguageValueName) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void SaveLanguageToRegistry(string languageId)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath, true))
                {
                    key.SetValue(LanguageValueName, languageId);
                }
            }
            catch
            {
                // Persistence is best-effort.
            }
        }
    }
}
