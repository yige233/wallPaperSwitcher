using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WallpaperSwitcher
{
    public class ConfigItem
    {
        public string Path { get; set; }
        public string Section { get; set; }
        public string Key { get; set; }
        public string Description { get; set; }
        public string DefaultValue { get; set; }
        public string CurrentValue { get; set; }

        public ConfigItem(string path, string desc, string defaultVal = "")
        {
            Path = path;
            Description = desc;
            DefaultValue = defaultVal;
            CurrentValue = defaultVal;
        }
    }
    public static class Config
    {
        // --- 1. P/Invoke 声明 (强制 Unicode) ---
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        private static string _iniPath;
        private static string _defaultSection;
        private static Dictionary<string, ConfigItem> _items = new Dictionary<string, ConfigItem>(StringComparer.OrdinalIgnoreCase);
        private static object _lock = new object();

        public static void Init(string iniPath, List<ConfigItem> items, string defaultSectionTag = "Settings")
        {
            _iniPath = iniPath;
            _defaultSection = defaultSectionTag;
            _items.Clear();

            foreach (var item in items)
            {
                ParsePath(item);
                if (!_items.ContainsKey(item.Path))
                {
                    _items.Add(item.Path, item);
                }
            }
            if (File.Exists(_iniPath)) { RefreshFromDisk(); }
            else { WriteAll(); }
        }

        private static void ParsePath(ConfigItem item)
        {
            int dotIndex = item.Path.IndexOf('.');
            if (dotIndex > 0)
            {
                item.Section = item.Path.Substring(0, dotIndex);
                item.Key = item.Path.Substring(dotIndex + 1);
            }
            else
            {
                item.Section = _defaultSection;
                item.Key = item.Path;
            }
        }

        private static void RefreshFromDisk()
        {
            foreach (var kvp in _items)
            {
                ConfigItem item = kvp.Value;
                StringBuilder sb = new StringBuilder(2048);
                GetPrivateProfileString(item.Section, item.Key, item.DefaultValue, sb, 2048, _iniPath);
                item.CurrentValue = sb.ToString();
            }
        }

        public static string Read(string path)
        {
            ConfigItem item;
            if (_items.TryGetValue(path, out item))
            {
                StringBuilder sb = new StringBuilder(2048);
                GetPrivateProfileString(item.Section, item.Key, item.CurrentValue, sb, 2048, _iniPath);
                string val = sb.ToString();
                item.CurrentValue = val;
                return val;
            }
            return "";
        }

        public static void Write(string path, string value)
        {
            string section = _defaultSection;
            string key = path;
            ConfigItem item;
            if (_items.TryGetValue(path, out item))
            {
                item.CurrentValue = value;
                section = item.Section;
                key = item.Key;
            }
            else
            {
                int dotIndex = path.IndexOf('.');
                if (dotIndex > 0)
                {
                    section = path.Substring(0, dotIndex);
                    key = path.Substring(dotIndex + 1);
                }
            }
            if (!File.Exists(_iniPath)) File.WriteAllText(_iniPath, "", Encoding.Unicode);
            WritePrivateProfileString(section, key, value, _iniPath);
        }

        public static string Text()
        {
            StringBuilder sb = new StringBuilder();

            var grouped = _items.Values
                .GroupBy(x => x.Section)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                sb.AppendLine(string.Format("[{0}]", group.Key));

                foreach (var item in group)
                {
                    if (!string.IsNullOrWhiteSpace(item.Description))
                    {
                        sb.AppendLine(string.Format("# {0}", item.Description));
                    }
                    sb.AppendLine(string.Format("{0}={1}", item.Key, item.CurrentValue));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static void WriteAll()
        {
            lock (_lock)
            {
                try
                {
                    string content = Text();
                    File.WriteAllText(_iniPath, content, Encoding.Unicode);
                }
                catch (Exception ex) { App.Log("保存全部配置失败: " + ex.Message); }
            }
        }
    }
}

