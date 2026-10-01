using System;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace ScamWYF.Modding.Core
{
    /// <summary>
    /// Config binding that survives a hand-edited file.
    /// </summary>
    /// <remarks>
    /// Config files for this game are meant to be edited in a text editor, so "MinMaxTokens =
    /// lots" has to mean something other than a mod that throws during Awake. Every read here is
    /// range-checked and falls back to a known-good value, complaining in the log when it does.
    ///
    /// BepInEx already gives each plugin its own .cfg named after its GUID, so keys cannot collide
    /// across mods. What this adds is not letting a bad value take the mod down with it.
    /// </remarks>
    public sealed class ModSettings
    {
        private readonly ConfigFile _file;
        private readonly ManualLogSource _log;
        private readonly string _modId;

        /// <summary>
        /// ConfigEntry does not expose its own definition, so the section/key each entry was bound
        /// under is remembered here. It is only used to name the setting in a warning.
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<object, string> _labels =
            new System.Collections.Generic.Dictionary<object, string>();

        public ModSettings(ConfigFile file, ManualLogSource log, string modId)
        {
            if (file == null) throw new ArgumentNullException("file");
            _file = file;
            _log = log;
            _modId = modId;
        }

        public ConfigFile File
        {
            get { return _file; }
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            var entry = _file.Bind(section, key, defaultValue, description);
            _labels[entry] = section + " / " + key;
            return entry;
        }

        /// <summary>Bind a key, for use with the shared hotkey registry.</summary>
        public ConfigEntry<UnityEngine.InputSystem.Key> BindKey(string section, string key,
            UnityEngine.InputSystem.Key defaultValue, string description)
        {
            var entry = _file.Bind(section, key, defaultValue, description);
            _labels[entry] = section + " / " + key;
            return entry;
        }

        private string Label(object entry)
        {
            string label;
            return _labels.TryGetValue(entry, out label) ? label : "a setting";
        }

        /// <summary>An int clamped into range. Out-of-range means someone edited the file.</summary>
        public int Int(ConfigEntry<int> entry, int min, int max)
        {
            int value;
            try
            {
                value = entry.Value;
            }
            catch (Exception ex)
            {
                _log.LogWarning(_modId + ": " + Label(entry) + " is unreadable (" + ex.Message +
                                "); using " + min + ".");
                return min;
            }

            if (value >= min && value <= max) return value;

            var clamped = Clamp(value, min, max);
            _log.LogWarning(_modId + ": " + Label(entry) + " is " + value + ", outside " + min + ".." + max +
                            "; using " + clamped + ".");
            return clamped;
        }

        /// <summary>A float clamped into range.</summary>
        public float Float(ConfigEntry<float> entry, float min, float max)
        {
            float value;
            try
            {
                value = entry.Value;
            }
            catch (Exception ex)
            {
                _log.LogWarning(_modId + ": " + Label(entry) + " is unreadable (" + ex.Message +
                                "); using " + min + ".");
                return min;
            }

            if (value >= min && value <= max) return value;

            var clamped = Clamp(value, min, max);
            _log.LogWarning(_modId + ": " + Label(entry) + " is " + value + ", outside " + min + ".." + max +
                            "; using " + clamped + ".");
            return clamped;
        }

        /// <summary>An enum, falling back when the file names a value that does not exist.</summary>
        public T EnumValue<T>(ConfigEntry<T> entry, T fallback) where T : struct
        {
            try
            {
                return entry.Value;
            }
            catch (Exception ex)
            {
                _log.LogWarning(_modId + ": " + Label(entry) + " is not a valid " + typeof(T).Name +
                                " (" + ex.Message + "); using " + fallback + ".");
                return fallback;
            }
        }

        /// <summary>A string that cannot be null, and is trimmed.</summary>
        public string Text(ConfigEntry<string> entry, string fallbackWhenBlank)
        {
            try
            {
                var value = entry.Value;
                if (string.IsNullOrEmpty(value) || value.Trim().Length == 0) return fallbackWhenBlank;
                return value.Trim();
            }
            catch
            {
                return fallbackWhenBlank;
            }
        }

        /// <summary>A string left exactly as typed, for things like header blobs.</summary>
        public string Raw(ConfigEntry<string> entry)
        {
            try
            {
                return entry.Value ?? "";
            }
            catch
            {
                return "";
            }
        }

        public bool Bool(ConfigEntry<bool> entry, bool fallback)
        {
            try
            {
                return entry.Value;
            }
            catch (Exception ex)
            {
                _log.LogWarning(_modId + ": " + Label(entry) + " is unreadable (" + ex.Message +
                                "); using " + fallback + ".");
                return fallback;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}