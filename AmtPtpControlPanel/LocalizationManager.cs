using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Xml.Linq;

namespace AmtPtpControlPanel
{
    /// <summary>
    /// Minimal, optional localization manager.
    ///
    /// It looks for a file named "Localization.xml" in the same directory as the
    /// executable. The file has this shape:
    ///
    /// <![CDATA[
    /// <Localization>
    ///   <Language id="en">
    ///     <String id="Main">Magic Trackpad 2 Control Panel</String>
    ///     ...
    ///   </Language>
    ///   <Language id="it">
    ///     <String id="Main">Pannello di controllo Magic Trackpad 2</String>
    ///     ...
    ///   </Language>
    /// </Localization>
    /// ]]>
    ///
    /// If the file is missing, malformed, or does not contain a language matching
    /// the current OS UI language, the manager stays inactive and the application
    /// behaves exactly as it always did.
    /// </summary>
    public static class LocalizationManager
    {
        private const string FileName = "AmtPtpControlPanel.Localization.xml";

        private static Dictionary<string, string> _strings;

        /// <summary>
        /// Gets a value indicating whether a usable localization was loaded.
        /// </summary>
        public static bool IsActive
        {
            get { return _strings != null; }
        }

        /// <summary>
        /// Loads Localization.xml from the executable directory, if present.
        /// </summary>
        public static void Initialize()
        {
            _strings = null;

            try
            {
                string directory = Path.GetDirectoryName(Application.ExecutablePath);
                if (string.IsNullOrEmpty(directory))
                    directory = AppDomain.CurrentDomain.BaseDirectory;

                string filePath = Path.Combine(directory, FileName);
                if (!File.Exists(filePath))
                    return;

                XDocument document = XDocument.Load(filePath);
                XElement language = FindMatchingLanguage(document);
                if (language == null)
                    return;

                Dictionary<string, string> strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (XElement element in language.Elements("String"))
                {
                    string id = (string)element.Attribute("id");
                    string value = element.Value;

                    if (string.IsNullOrEmpty(id) || value == null)
                        continue;

                    value = value.Trim();
                    if (value.Length == 0)
                        continue;

                    strings[id] = value;
                }

                if (strings.Count == 0)
                    return;

                _strings = strings;
            }
            catch
            {
                // If anything goes wrong, silently stay inactive.
                _strings = null;
            }
        }

        /// <summary>
        /// Returns the localized string for <paramref name="key"/>, or
        /// <paramref name="fallback"/> when localization is inactive or the key
        /// is missing.
        /// </summary>
        public static string GetString(string key, string fallback = null)
        {
            if (_strings == null || key == null)
                return fallback;

            string value;
            return _strings.TryGetValue(key, out value) ? value : fallback;
        }

        /// <summary>
        /// Recursively applies localized text to <paramref name="root"/> and all
        /// of its child controls, using each control's Name as the lookup key.
        /// </summary>
        public static void Apply(Control root)
        {
            if (_strings == null || root == null)
                return;

            ApplyText(root);

            foreach (Control child in root.Controls)
                ApplyToControl(child);
        }

        private static void ApplyToControl(Control control)
        {
            ApplyText(control);

            foreach (Control child in control.Controls)
                ApplyToControl(child);
        }

        private static void ApplyText(Control control)
        {
            if (control == null || string.IsNullOrEmpty(control.Name))
                return;

            string text = GetString(control.Name, null);
            if (text != null)
                control.Text = text;
        }

        private static XElement FindMatchingLanguage(XDocument document)
        {
            XElement root = document.Root;
            if (root == null)
                return null;

            List<XElement> languages = new List<XElement>(root.Elements("Language"));
            if (languages.Count == 0)
                return null;

            foreach (string candidate in GetLanguageCandidates())
            {
                foreach (XElement language in languages)
                {
                    string id = (string)language.Attribute("id");
                    if (string.Equals(id, candidate, StringComparison.OrdinalIgnoreCase))
                        return language;
                }
            }

            return null;
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
    }
}
