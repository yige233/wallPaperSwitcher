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
        private static string _iniPath;
        private static string _defaultSection;
        private static Dictionary<string, ConfigItem> _items = new Dictionary<string, ConfigItem>(StringComparer.OrdinalIgnoreCase);
        private static object _lock = new object();
        private static FileSystemWatcher _configWatcher;
        public static void Init(string iniPath, List<ConfigItem> items, string defaultSectionTag = "Settings")
        {
            string directory = Path.GetDirectoryName(iniPath);
            string filename = Path.GetFileName(iniPath);

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

            // 监听配置文件，更新时同步更新内存中的值。如果启动监听后内部会用到write()，需要上锁防止死循环
            _configWatcher = new FileSystemWatcher(directory);
            _configWatcher.Filter = filename;
            _configWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime;
            _configWatcher.Changed += new FileSystemEventHandler(OnConfigFileChanged);
            _configWatcher.EnableRaisingEvents = true;

        }
        private static void OnConfigFileChanged(object sender, FileSystemEventArgs e) { RefreshFromDisk(); }
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
                Kernel32.GetPrivateProfileString(item.Section, item.Key, item.DefaultValue, sb, 2048, _iniPath);
                item.CurrentValue = sb.ToString();
            }
        }

        public static string Read(string path)
        {
            ConfigItem item;
            if (_items.TryGetValue(path, out item)) { return item.CurrentValue; }
            return "";
        }

        public static void Write(string path, string value)
        {
            if (!File.Exists(_iniPath)) { WriteAll(); return; }
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
            Kernel32.WritePrivateProfileString(section, key, value, _iniPath);
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
                catch (Exception ex) { App.LogForce("保存全部配置失败: {0}", ex.Message); }
            }
        }
    }
}

