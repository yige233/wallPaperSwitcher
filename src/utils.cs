using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.Reflection;
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

            // 判定逻辑: 窗口边缘是否贴合或超出显示器物理边缘
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
    public static class Downloader
    {
        static Downloader()
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
        }
        private static HttpClient CreateClient(bool useProxy)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = useProxy,
                Proxy = null // null = 使用系统/IE代理
            };
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(string.Format("{0}/{1}", App.AppName, App.AppVersion));
            // 设置超时
            client.Timeout = TimeSpan.FromSeconds(20);
            return client;
        }
        public static byte[] Download(string url)
        {
            byte[] result;
            try
            {
                using (var client = CreateClient(true))
                {
                    result = ExecuteDownload(client, url);
                    return result;
                }
            }
            catch (Exception ex) { LogDownloadError(url, ex, "下载失败"); }
            try
            {
                using (var client = CreateClient(false))
                {
                    result = ExecuteDownload(client, url);
                    return result;
                }
            }
            catch (Exception ex) { LogDownloadError(url, ex, "直连下载失败"); }
            return null;
        }
        private static byte[] ExecuteDownload(HttpClient client, string url)
        {
            HttpResponseMessage response = client.GetAsync(url).Result;
            response.EnsureSuccessStatusCode();
            string finalUrl = response.RequestMessage.RequestUri.ToString();
            App.Log("正在下载URL: {0}", finalUrl);
            using (Stream stream = response.Content.ReadAsStreamAsync().Result)
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        private static void LogDownloadError(string url, Exception ex, string prefix)
        {
            AggregateException ae = ex as AggregateException;
            if (ae != null)
            {
                App.LogForce("{0} [{1}]: {2}", prefix, url, ae.Message);
                foreach (var inner in ae.InnerExceptions) { App.LogForce("  -> {0}: {1}", inner.GetType().Name, inner.Message); }
            }
            else { App.LogForce("{0} [{1}]: {2}", prefix, url, ex.Message); }
        }
    }
    partial class App
    {
        public static bool LogEnabled()
        {
            bool enableLog = false;
            bool.TryParse(Config.Read("Log"), out enableLog);
            return enableLog;
        }
        public static void Log(string format, params object[] args)
        {
            if (!LogEnabled()) return;
            string line = string.Format(format, args);
            if (isConsole) { Console.WriteLine(line); }
            File.AppendAllText(LogPath, line + "\r\n");
        }

        public static void LogForce(string format, params object[] args)
        {
            string line = string.Format(format, args);
            if (isConsole) { Console.WriteLine(line); }
            File.AppendAllText(LogPath, line + "\r\n");
        }
        private static string FormatLog(string format, object[] args)
        {
            string userMsg = (args == null || args.Length == 0) ? format : string.Format(format, args);
            string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return string.Format("[{0}] {1}", timeStr, userMsg);
        }
    }

}