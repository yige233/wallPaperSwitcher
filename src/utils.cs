using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace WallpaperSwitcher
{
    public static class Win32
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
        public const int SYMBOLIC_LINK_FLAG_FILE = 0x0;
        public const int SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 0x2;
        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId = ATTACH_PARENT_PROCESS);
        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();
        [DllImport("kernel32.dll")]
        public static extern bool FreeConsole();
        public const int ATTACH_PARENT_PROCESS = -1;
    };
    public static class SystemUtils
    {
        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        public static void RunAsAdmin(string args, string errorText)
        {
            var exeName = Process.GetCurrentProcess().MainModule.FileName;
            ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
            startInfo.Arguments = args;
            startInfo.Verb = "runas";
            startInfo.UseShellExecute = true;
            try
            {
                Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(errorText, App.AppName + " - 需要管理员权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        public static bool IsUserBusy()
        {
            if (IsLocked()) return true;

            // 2. 获取当前用户正在交互的窗口
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return false; // 无焦点，视为不忙

            // 3. 排除桌面和任务栏
            if (IsDesktopOrShell(hWnd)) return false;

            if (IsZoomed(hWnd)) return true;
            if (IsFullScreen(hWnd)) return true;

            return false;
        }
        public static void SendEnterKey()
        {
            IntPtr hWnd = GetConsoleWindow();
            if (hWnd != IntPtr.Zero) { PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero); }
        }
        public static void RunCmdCommand(string fileName, string args)
        {
            Process process = new Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = args;
            process.StartInfo.UseShellExecute = false; // 必须为false才能重定向输出
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true; // 同时也捕获错误
            process.StartInfo.CreateNoWindow = true; // 不显示黑框
            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(output)) Console.WriteLine(String.Format("[{0}] {1}", fileName, output.Trim()));
            if (!string.IsNullOrEmpty(error)) Console.WriteLine(String.Format("[{0} Error] {1}", fileName, error.Trim()));
        }
        public static bool IsLocked()
        {
            IntPtr hDesktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
            if (hDesktop == IntPtr.Zero) return true;
            CloseDesktop(hDesktop);
            return false;
        }

        public static bool IsFullScreen(IntPtr hWnd)
        {
            RECT appRect;
            if (!GetWindowRect(hWnd, out appRect)) return false;

            // 获取窗口所在的显示器
            IntPtr hMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTOPRIMARY);
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);
            if (!GetMonitorInfo(hMonitor, ref mi)) return false;

            // 判定逻辑：窗口边缘是否贴合或超出显示器物理边缘
            return appRect.Top <= mi.rcMonitor.Top &&
                    appRect.Left <= mi.rcMonitor.Left &&
                    appRect.Bottom >= mi.rcMonitor.Bottom &&
                    appRect.Right >= mi.rcMonitor.Right;
        }

        private static bool IsDesktopOrShell(IntPtr hWnd)
        {
            // 检查特殊句柄
            if (hWnd == GetShellWindow()) return true;

            // 检查类名
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            string className = sb.ToString();

            // WorkerW / Progman = 桌面背景
            // Shell_TrayWnd = 任务栏
            return className == "WorkerW" || className == "Progman" || className == "Shell_TrayWnd";
        }
        public static bool IsMonitorActive() { return MonitorStateWatcher.IsScreenOn; }

        public static bool IsUserIdle(int thresholdSeconds)
        {
            LASTINPUTINFO lastInput = new LASTINPUTINFO();
            lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

            if (!GetLastInputInfo(ref lastInput))
            {
                // 如果 API 调用失败，保守起见认为用户不空闲
                return false;
            }

            // Environment.TickCount 获取系统启动后的毫秒数
            // 使用 unchecked 处理 TickCount 在运行 49.7 天后的回绕问题
            int elapsedTicks = unchecked(Environment.TickCount - (int)lastInput.dwTime);

            // 转换为秒
            int idleSeconds = elapsedTicks / 1000;

            return idleSeconds >= thresholdSeconds;
        }

        private static class MonitorStateWatcher
        {
            public static volatile bool IsScreenOn = true;
            private static Thread _watcherThread;
            private static ManualResetEvent _initDone = new ManualResetEvent(false);

            static MonitorStateWatcher()
            {
                _watcherThread = new Thread(WatcherLoop);
                _watcherThread.IsBackground = true; // 设为后台线程，不阻止进程退出
                _watcherThread.SetApartmentState(ApartmentState.STA);
                _watcherThread.Start();
                _initDone.WaitOne(500);
            }

            private static void WatcherLoop()
            {
                // 1. 创建隐藏的消息接收窗口 (使用 "Static" 类名无需注册)
                IntPtr hWnd = CreateWindowEx(0, "Static", "MonitorWatcher", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

                if (hWnd == IntPtr.Zero) return;

                // 2. 注册电源设置通知: GUID_MONITOR_POWER_ON
                Guid guidMonitorOn = new Guid("02731015-4510-4526-99e6-e5a17ebd1aea");
                IntPtr hNotify = RegisterPowerSettingNotification(hWnd, ref guidMonitorOn, 0);

                // 3. 消息循环
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0))
                {
                    if (msg.message == WM_POWERBROADCAST && msg.wParam.ToInt32() == PBT_POWERSETTINGCHANGE)
                    { ProcessPowerSetting(msg.lParam); }
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (hNotify != IntPtr.Zero) UnregisterPowerSettingNotification(hNotify);
            }

            private static void ProcessPowerSetting(IntPtr lParam)
            {
                try
                {
                    POWERBROADCAST_SETTING ps = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(lParam, typeof(POWERBROADCAST_SETTING));
                    if (ps.DataLength > 0 && ps.Data != null)
                    { IsScreenOn = ps.Data[0] != 0; }
                }
                finally { _initDone.Set(); }
            }
        }
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr Handle);
        private const int WM_POWERBROADCAST = 0x0218;
        private const int PBT_POWERSETTINGCHANGE = 0x8013;
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public int time; public POINT pt; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POWERBROADCAST_SETTING { public Guid PowerSetting; public int DataLength; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public byte[] Data; }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);
        private const uint MONITOR_DEFAULTTOPRIMARY = 1;
        private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        private const uint WM_KEYDOWN = 0x0100;
        private const int VK_RETURN = 0x0D;
    };
    partial class App
    {
        public static string GetConfigFilePath()
        {
            string envPath = Environment.GetEnvironmentVariable("WS_CONFIG");
            if (!string.IsNullOrEmpty(envPath))
            {
                envPath = envPath.Trim('\"', '\'');
                try
                { return Path.Combine(Path.GetFullPath(envPath), "config.ini"); }
                catch { }
            }
            return Path.Combine(BaseDir, "config.ini");
        }
        public static void Log(string msg = "", bool forceLog = false)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg;
            if (isConsole) { Console.WriteLine(line); }
            if (forceLog)
            {
                File.AppendAllText(LogPath, line + "\r\n");
                return;
            }
            try
            {
                bool enableLog;
                bool.TryParse(Config.Read("Log"), out enableLog);
                if (!enableLog) return;
                File.AppendAllText(LogPath, line + "\r\n");
            }
            catch { }
        }

        private static readonly HttpClient _httpClient = new HttpClient();
        public static byte[] Download(string url)
        {
            try
            {
                HttpResponseMessage response = _httpClient.GetAsync(url).Result;
                response.EnsureSuccessStatusCode();
                string finalUrl = response.RequestMessage.RequestUri.ToString();
                Log("真实URL：" + finalUrl);
                return response.Content.ReadAsByteArrayAsync().Result;
            }
            catch (AggregateException ae)
            {
                Log("下载 " + url + " 失败: " + ae.Message, true);
                foreach (var ex in ae.InnerExceptions)
                {
                    Log(string.Format("下载异常(内部): {0} - {1}", ex.GetType().Name, ex.Message), true);
                    if (ex.InnerException != null) { Log(string.Format("  -> 根源: {0}", ex.InnerException.Message), true); }
                }
                return null;
            }

        }
    }
}