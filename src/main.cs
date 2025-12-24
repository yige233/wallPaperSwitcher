using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WallpaperSwitcher
{
    partial class App
    {
        public static string AppName = "WallpaperSwitcher";
        private static Assembly currentAssembly = Assembly.GetExecutingAssembly();
        public static string AppDesc = currentAssembly.GetCustomAttribute<AssemblyDescriptionAttribute>().Description;
        public static string ExecutableName = currentAssembly.GetName().Name;
        public static string AppVersion = currentAssembly.GetName().Version.ToString();
        public static string GithubURL = "https://github.com/yige233/wallPaperSwitcher";
        public static string SwitchEventName = string.Format("Global\\{0}SwitchEventSignal", AppName);
        public static string QuitEventName = string.Format("Global\\{0}QuitEventSignal", AppName);
        public static string ServiceMutex = string.Format("Global\\{0}ServiceMutex", AppName);
        public static string ExecutablePath = Process.GetCurrentProcess().MainModule.FileName;
        public static string BaseDir = Path.GetDirectoryName(ExecutablePath);
        public static string LogPath = Path.Combine(BaseDir, AppName + ".log");
        static string ConfigPath = GetConfigFilePath();
        static Mutex appMutex;
        public static bool isConsole = Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
        public static string GetConfigFilePath()
        {
            string envPath = Environment.GetEnvironmentVariable("WS_CONFIG");
            if (!string.IsNullOrEmpty(envPath))
            {
                envPath = envPath.Trim('\"', '\'');
                try { return Path.Combine(Path.GetFullPath(envPath), "config.ini"); }
                catch { }
            }
            return Path.Combine(BaseDir, "config.ini");
        }
        static void QuitService()
        {
            try { using (var wh = EventWaitHandle.OpenExisting(QuitEventName)) { wh.Set(); } } catch { }
        }
        static void ManualSwitchWallpaper()
        {
            try { using (var wh = EventWaitHandle.OpenExisting(SwitchEventName)) { wh.Set(); } } catch { }
        }
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // 1. 定义配置大纲 (Schema)
                var configSchema = new List<ConfigItem>()
                {
                new ConfigItem("BasePath", "要存放壁纸的文件夹路径。"),
                new ConfigItem("ImageUrl", "壁纸图片的URL。"),
                new ConfigItem("LockScreen", "是否同时将当前壁纸设为锁屏壁纸。", "true"),
                new ConfigItem("IntervalSeconds", "壁纸切换间隔（秒）。", "300"),
                new ConfigItem("IdleThresholdSeconds", "用户空闲时间（秒）。超过该时间后，将停止切换壁纸。", "3600"),
                new ConfigItem("ExtraFormat", "允许作为桌面壁纸的图像格式。支持webp,heic,avif。若包含多个值，使用英文逗号分隔。"),
                new ConfigItem("Log", "是否启用日志。", "false"),
                new ConfigItem("JPGQuality", "转换后的图像质量（0-100）。", "95"),
                new ConfigItem("RetryAfter", "后台准备壁纸失败后，重试的间隔（秒）。","60"),
                new ConfigItem("CurrentSlot", "当前壁纸使用的槽位。不要自行修改。")
                };
                // 2. 初始化 Config 类
                Config.Init(ConfigPath, configSchema, "Settings");

                string flag = args.Length > 0 ? args[0] : "";
                string value = args.Length > 1 ? args[1] : "";

                if (isConsole) { Console.WriteLine(""); }

                if (flag == "-c" || flag == "--config")
                {
                    if (value == "")
                    {
                        if (isConsole) { Console.WriteLine(Config.Text()); }
                        else { Process.Start(ConfigPath); }
                        return;
                    }

                    string configName = value.Split('=')[0];
                    string ConfigValue = value.Split('=')[1];
                    Config.Write(configName, ConfigValue);
                    return;
                }
                if (flag == "-q" || flag == "--quit")
                {
                    QuitService();
                    MessageBox.Show(AppName + " 已不再运行。", AppName, MessageBoxButtons.OK);
                    return;
                }
                if (flag == "-j" || flag == "--jumplist")
                {
                    JumpListInjector.Run();
                    return;
                }
                if (flag == "-h" || flag == "--help")
                {
                    string format = @"{0} v{1}
{2}
用法: {3} [参数]

参数:
-c, --config [<path>=<value>]      修改配置文件。如果不添加第二个参数，则是打开配置文件。
-j, --jumplist                     为固定到任务栏的快捷方式设置跳转列表，
-q, --quit                         退出正在运行的后台服务。
-h, --help                         显示帮助。
不使用任何参数，即为启动后台服务。若已经存在一个正在运行的后台服务，则执行手动切换壁纸。";
                    string help = string.Format(format, AppName, AppVersion, AppDesc, ExecutableName);
                    if (isConsole) { Console.WriteLine(help); }
                    else { Process.Start(GithubURL); }
                    return;
                }
                if (isConsole) { Console.WriteLine("{0} v{1}\n使用 -h 参数查看帮助。", AppName, AppVersion); }
                if (flag == "")
                {
                    bool createdNew;
                    appMutex = new Mutex(true, ServiceMutex, out createdNew);

                    // 如果已经运行，则触发切换壁纸信号，然后退出
                    if (!createdNew) { ManualSwitchWallpaper(); return; }

                    // 监听 %AppData%\Microsoft\Windows\Themes\TranscodedWallpaper，也就是壁纸缓存文件的变化。若产生变化，
                    // 说明用户点击了桌面右键菜单中的“下一个桌面背景”,我们触发自己的切换壁纸逻辑，覆盖掉系统的实际上并无作用的壁纸切换。
                    WallpaperEngine.OnUserChange(new WallpaperChangeHandler(ManualSwitchWallpaper));

                    RunServiceMode();
                }
            }
            catch (Exception ex)
            {
                if (isConsole) { Console.WriteLine(ex); }
                LogForce("程序运行时出现致命错误: {0}", ex.ToString());
            }
        }
    }
}