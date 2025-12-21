using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace WallpaperSwitcher
{
    partial class Program
    {
        public static string AppName = "WallpaperSwitcher";
        public static string SwitchEventName = "Global\\" + AppName + "SwitchEventSignal";
        public static string QuitEventName = "Global\\" + AppName + "QuitEventSignal";

        public static bool isConsole = Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
        static void ManualSwitchWallpaper()
        {
            try { using (var wh = EventWaitHandle.OpenExisting(SwitchEventName)) { wh.Set(); } }
            catch { }
        }
        static void QuitService()
        {
            try { using (var wh = EventWaitHandle.OpenExisting(QuitEventName)) { wh.Set(); } }
            catch { }
        }
        static void InitConfig()
        {
            string config = @"[Settings]
# 要存放壁纸的文件夹路径。
BasePath=
# 壁纸图片的URL。
ImageUrl=
# 是否同时将当前壁纸设为锁屏壁纸。
LockScreen=true
# 壁纸切换间隔（秒）。
IntervalSeconds=300
# 用户空闲时间（秒）。超过该时间后，将停止切换壁纸。
IdleThresholdSeconds=3600
# 是否启用日志。
Log=false
# 转换后的图像质量（0-100）。
JPGQuality=95
# 当前壁纸使用的槽位。不要自行修改。
CurrentSlot=Slot_1
";

            File.WriteAllText(ConfigPath, config, Encoding.Unicode);
            Process.Start(ConfigPath);
        }
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                string flag = args.Length > 0 ? args[0] : "";
                string value = args.Length > 1 ? args[1] : "";
                if (flag == "-c" || flag == "--config")
                {
                    if (value == "") { Process.Start(ConfigPath); return; }

                    string configName = value.Split('=')[0];
                    string ConfigValue = value.Split('=')[1];
                    WriteIni(configName, ConfigValue);

                    return;
                }
                if (flag == "-s" || flag == "--switch")
                {
                    ManualSwitchWallpaper();
                    return;
                }
                if (flag == "-q" || flag == "--quit")
                {
                    QuitService();
                    MessageBox.Show(AppName + " 已不再运行。", AppName, MessageBoxButtons.OK);
                    return;
                }
                if (flag == "-h" || flag == "--help")
                {
                    string[] help = new string[]
                    {
                        AppName + " v" + Assembly.GetExecutingAssembly().GetName().Version,
                        "",
                        "用法: " + AppName + " [参数]",
                        "",
                        "参数:",
                        "-c, --config [<path>=<value>]      修改配置文件。如果不添加第二个参数，则是打开配置文件。",
                        "-s, --switch                       手动切换壁纸。",
                        "-q, --quit                         退出正在运行的后台服务。",
                        "-h, --help                         显示帮助。",
                        "不使用任何参数，即为启动后台服务。",
                    };
                    MessageBox.Show(string.Join("\n", help), AppName + " - 帮助", MessageBoxButtons.OK);
                    return;
                }
                if (isConsole) { Console.WriteLine("\n" + AppName + " v" + Assembly.GetExecutingAssembly().GetName().Version + "\n使用 -h 参数查看帮助。"); }
                if (flag == "") { RunServiceMode(); }
            }
            finally
            {
                SystemUtils.SendEnterKey();
            }
        }
    }
}