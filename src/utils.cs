using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WallpaperSwitcher
{
    public static class Kernel32
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

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        public static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        public static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

    };
    public static class User32
    {
        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseDesktop(IntPtr hDesktop);
        public const uint MONITOR_DEFAULTTOPRIMARY = 1;
        public const uint DESKTOP_SWITCHDESKTOP = 0x0100;
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        }

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
        [DllImport("user32.dll")]
        public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        public static extern bool TranslateMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);
        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        public struct POWERBROADCAST_SETTING { public Guid PowerSetting; public uint DataLength; public byte Data; }
        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASS { public uint style; public WndProcDelegate lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string lpszMenuName; public string lpszClassName; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int pt_x; public int pt_y; }

    }
    public static class Shell32
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
        [DllImport("shell32.dll", PreserveSig = true)]
        public static extern int SHCreateShellItemArrayFromShellItem([MarshalAs(UnmanagedType.Interface)] IShellItem psi, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppsiItemArray);
    };
    public static class Utils
    {
        public static bool IsUserBusy()
        {
            // 2. 获取当前用户正在交互的窗口
            IntPtr hWnd = User32.GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return false; // 无焦点，视为不忙
            // 3. 排除桌面和任务栏
            if (IsDesktopOrShell(hWnd)) return false;
            if (User32.IsZoomed(hWnd)) return true;
            if (IsFullScreen(hWnd)) return true;

            return false;
        }
        public static bool IsLocked()
        {
            IntPtr hDesktop = User32.OpenInputDesktop(0, false, User32.DESKTOP_SWITCHDESKTOP);
            if (hDesktop == IntPtr.Zero) return true;
            User32.CloseDesktop(hDesktop);
            return false;
        }

        public static bool IsFullScreen(IntPtr hWnd)
        {
            User32.RECT appRect;
            if (!User32.GetWindowRect(hWnd, out appRect)) return false;

            // 获取窗口所在的显示器
            IntPtr hMonitor = User32.MonitorFromWindow(hWnd, User32.MONITOR_DEFAULTTOPRIMARY);
            User32.MONITORINFO mi = new User32.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);
            if (!User32.GetMonitorInfo(hMonitor, ref mi)) return false;

            // 判定逻辑: 窗口边缘是否贴合或超出显示器物理边缘
            return appRect.Top <= mi.rcMonitor.Top &&
                    appRect.Left <= mi.rcMonitor.Left &&
                    appRect.Bottom >= mi.rcMonitor.Bottom &&
                    appRect.Right >= mi.rcMonitor.Right;
        }

        private static bool IsDesktopOrShell(IntPtr hWnd)
        {
            // 检查特殊句柄
            if (hWnd == User32.GetShellWindow()) return true;

            // 检查类名
            StringBuilder sb = new StringBuilder(256);
            User32.GetClassName(hWnd, sb, sb.Capacity);
            string className = sb.ToString();

            // WorkerW / Progman = 桌面背景 Shell_TrayWnd = 任务栏
            return className == "WorkerW" || className == "Progman" || className == "Shell_TrayWnd";
        }
        public static bool IsUserIdle(int thresholdSeconds)
        {
            User32.LASTINPUTINFO lastInput = new User32.LASTINPUTINFO();
            lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

            // 如果 API 调用失败，保守起见认为用户不空闲
            if (!User32.GetLastInputInfo(ref lastInput)) { return false; }

            // Environment.TickCount 获取系统启动后的毫秒数
            // 使用 unchecked 处理 TickCount 在运行 49.7 天后的回绕问题
            int elapsedTicks = unchecked(Environment.TickCount - (int)lastInput.dwTime);
            int idleSeconds = elapsedTicks / 1000;

            return idleSeconds >= thresholdSeconds;
        }
        public static bool IsRDPSession()
        {
            const int SM_REMOTESESSION = 0x1000;
            return User32.GetSystemMetrics(SM_REMOTESESSION) != 0;
        }
        public static bool IsMonitorActivated() { return _currentMonitorState != 0; }
        public static void StartMonitorListening()
        {
            if (_isListening) return;

            Thread listenerThread = new Thread(MessageLoop);
            listenerThread.IsBackground = true;
            listenerThread.Name = "PowerMonitorThread";
            listenerThread.Start();
            _isListening = true;
        }
        private static volatile int _currentMonitorState = 1; // 默认为开启
        private static bool _isListening = false;

        static Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        const int WM_POWERBROADCAST = 0x0218;
        const int PBT_POWERSETTINGCHANGE = 0x8013;
        const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        private static User32.WndProcDelegate _wndProcDelegate;

        private static void MessageLoop()
        {
            string className = string.Format("{0}_PowerWatcher_{1}", App.AppName, Guid.NewGuid().ToString("N"));
            _wndProcDelegate = new User32.WndProcDelegate(CustomWndProc);

            User32.WNDCLASS wc = new User32.WNDCLASS();
            wc.lpfnWndProc = _wndProcDelegate;
            wc.lpszClassName = className;

            User32.RegisterClass(ref wc);

            // 创建隐藏窗口
            IntPtr hWnd = User32.CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (hWnd != IntPtr.Zero)
            {
                User32.RegisterPowerSettingNotification(hWnd, ref GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE);
                User32.MSG msg;
                while (User32.GetMessage(out msg, IntPtr.Zero, 0, 0))
                {
                    User32.TranslateMessage(ref msg);
                    User32.DispatchMessage(ref msg);
                }
            }
        }

        private static IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_POWERBROADCAST && wParam.ToInt64() == PBT_POWERSETTINGCHANGE)
            {
                try
                {
                    User32.POWERBROADCAST_SETTING ps = (User32.POWERBROADCAST_SETTING)Marshal.PtrToStructure(lParam, typeof(User32.POWERBROADCAST_SETTING));
                    if (ps.PowerSetting == GUID_CONSOLE_DISPLAY_STATE) { _currentMonitorState = ps.Data; }
                }
                catch { }
            }
            return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
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
            string line = FormatLog(format, args);
            if (isConsole) { Console.WriteLine(line); }
            File.AppendAllText(LogPath, line + "\r\n");
        }

        public static void LogForce(string format, params object[] args)
        {
            string line = FormatLog(format, args);
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