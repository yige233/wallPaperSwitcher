using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace WallpaperApp {

    public static class Win32 {
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        public static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        public static extern int WritePrivateProfileString(string section, string key, string val, string filePath);

        // 【核心修复】将返回值从 bool 改为 Int32，严格遵循 Win32 API
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern Int32 CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }

    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IShellItem { }
    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IShellItemArray { }
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IDesktopWallpaper {
        void SetWallpaper(string monitorID, string wallpaper); string GetWallpaper(string monitorID); string GetMonitorDevicePathAt(uint monitorIndex); uint GetMonitorDevicePathCount(); void GetMonitorRECT(string monitorID, out Rect displayRect); void SetBackgroundColor(uint color); uint GetBackgroundColor(); void SetPosition(int position); int GetPosition();
        void SetSlideshow(IShellItemArray items); void GetSlideshow(out IShellItemArray items); void SetSlideshowOptions(int options, uint slideshowTick); void GetSlideshowOptions(out int options, out uint slideshowTick); void AdvanceSlideshow(string monitorID, int direction); void GetStatus(out int state); bool Enable(bool enable);
    }
    
    public class WallpaperEngine {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] 
        public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
        [DllImport("shell32.dll", PreserveSig = true)] 
        public static extern int SHCreateShellItemArrayFromShellItem([MarshalAs(UnmanagedType.Interface)] IShellItem psi, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppsiItemArray);
        public static void SetFolder(string path) {
            Program.Log("API Call: SetSlideshow -> " + path);
            Type type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));
            IDesktopWallpaper wallpaper = (IDesktopWallpaper)Activator.CreateInstance(type);
            Guid gItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"); Guid gArr = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
            IShellItem item;
            int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref gItem, out item);
            if (hr != 0) throw new Exception("SHCreateItem failed: " + hr);
            IShellItemArray arr;
            hr = SHCreateShellItemArrayFromShellItem(item, ref gArr, out arr);
            if (hr != 0) throw new Exception("SHCreateShellItemArray failed: " + hr);
            wallpaper.SetSlideshow(arr);
        }
    }

    public class Program {
        static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        static string LogPath = Path.Combine(BaseDir, "debug.log");
        static string ConfigPath = Path.Combine(BaseDir, "config.ini");
        static Mutex appMutex;
        static EventWaitHandle triggerEvent;

        public static void Log(string msg) {
            try { 
                string enableLogStr = ReadIni("Log", "false");
                bool enableLog = false;
                bool.TryParse(enableLogStr, out enableLog);
                if (!enableLog) return;
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg + "\r\n";
                File.AppendAllText(LogPath, line); 
            } catch {}
        }

        static string ReadIni(string key, string defVal) {
            StringBuilder sb = new StringBuilder(1024);
            Win32.GetPrivateProfileString("Settings", key, defVal, sb, 1024, ConfigPath);
            return sb.ToString().Trim();
        }

        static void WriteIni(string key, string val) {
            Win32.WritePrivateProfileString("Settings", key, val, ConfigPath);
        }

        // 下载逻辑
        static bool Download(string targetDir, string url) {
            try {
                if (Directory.Exists(targetDir)) {
                    foreach(var f in Directory.GetFiles(targetDir)) try { File.Delete(f); } catch {}
                } else {
                    Directory.CreateDirectory(targetDir);
                }

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (WebClient wc = new WebClient()) {
                    string finalUrl = url + "&t=" + DateTime.Now.Ticks; 
                    byte[] data = wc.DownloadData(finalUrl);
                    using (MemoryStream ms = new MemoryStream(data)) 
                    using (Bitmap bmp = new Bitmap(ms)) {
                        string p1 = Path.Combine(targetDir, "wp1.jpg");
                        string p2 = Path.Combine(targetDir, "wp2.jpg");
                        
                        bmp.Save(p1, ImageFormat.Jpeg);
                        
                        // --- 符号链接逻辑 (mklink.exe) ---
                        bool linkSuccess = false;
                        try {
                            if (File.Exists(p2)) File.Delete(p2);

                            // 1. 配置进程启动信息
                            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                            startInfo.FileName = "cmd.exe";
                            
                            // 2. 构造命令: mklink "新文件" "源文件"
                            // 必须用引号包裹路径，防止路径中有空格
                            startInfo.Arguments = string.Format("/C mklink \"{0}\" \"{1}\"", p2, p1);
                            
                            // 3. 【关键】设置无窗口运行
                            startInfo.CreateNoWindow = true;
                            startInfo.UseShellExecute = false;
                            startInfo.RedirectStandardError = true; // 捕获错误信息

                            // 4. 启动进程并等待
                            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)) {
                                string error = process.StandardError.ReadToEnd(); // 读取错误流
                                process.WaitForExit();
                                
                                // 5. 检查退出码 (0 = 成功)
                                if (process.ExitCode == 0) {
                                    linkSuccess = true;
                                } else {
                                    Log("创建符号链接失败，退出代码：" + process.ExitCode);
                                }
                            }
                        } catch (Exception ex) {
                             Log("创建符号链接时出现错误： " + ex.Message);
                        }

                        if (!linkSuccess) {
                            Log("使用拷贝文件代替符号链接");
                            File.Copy(p1, p2, true);
                        }
                    }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return true;
            } catch (Exception ex) {
                Log("下载图像失败: " + ex.Message);
                return false;
            }
        }

        [STAThread]
        static void Main(string[] args) {
            if (args.Length > 0 && args[0] == "-s") { RunSwitchMode(); } 
            else { RunServiceMode(); }
        }

        static void RunSwitchMode() {
            try {
                using(var wh = EventWaitHandle.OpenExisting("Global\\AutoWallpaperEventSignal")) {
                    wh.Set();
                }
            } catch {}
        }

        static void RunServiceMode() {
            try {
                Log("=== 服务启动 ===");

                bool createdNew;
                appMutex = new Mutex(true, "Global\\AutoWallpaperServiceMutex", out createdNew);
                if (!createdNew) { return; }

                triggerEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\AutoWallpaperEventSignal");

                if (!File.Exists(ConfigPath)) { Log("找不到config.ini。"); return; }

                string BasePathRaw = ReadIni("BasePath", "");
                string ImageUrl = ReadIni("ImageUrl", "");
                string intStr = ReadIni("IntervalSeconds", "300");
                string CurrentSlot = ReadIni("CurrentSlot", "Slot_2");
                
                int Interval = 300;
                int.TryParse(intStr, out Interval);

                if (string.IsNullOrEmpty(BasePathRaw) || string.IsNullOrEmpty(ImageUrl)) {
                    Log("解析config.ini失败。");
                    return;
                }

                string BasePath = Path.GetFullPath(BasePathRaw);
                Log("加载配置成功。当前壁纸更新间隔：" + Interval);

                string Slot1 = Path.Combine(BasePath, "Slot_1");
                string Slot2 = Path.Combine(BasePath, "Slot_2");
                Directory.CreateDirectory(Slot1); Directory.CreateDirectory(Slot2);

                string NextSlotDir, NextSlotName;
                if (CurrentSlot == "Slot_1") { NextSlotDir = Slot2; NextSlotName = "Slot_2"; }
                else { NextSlotDir = Slot1; NextSlotName = "Slot_1"; }

                if (Directory.GetFiles(NextSlotDir, "*.jpg").Length == 0) {
                    Download(NextSlotDir, ImageUrl);
                }

                while (true) {
                    try {
                        if (Directory.GetFiles(NextSlotDir, "*.jpg").Length > 0) {
                            WallpaperEngine.SetFolder(NextSlotDir);
                            WriteIni("CurrentSlot", NextSlotName);
                        }

                        string TargetDir, TargetName;
                        if (NextSlotName == "Slot_1") { TargetDir = Slot2; TargetName = "Slot_2"; }
                        else { TargetDir = Slot1; TargetName = "Slot_1"; }

                        Download(TargetDir, ImageUrl);

                        bool signaled = triggerEvent.WaitOne(Interval * 1000);
                        if (signaled) Log("触发了手动壁纸切换。");

                        NextSlotDir = TargetDir;
                        NextSlotName = TargetName;
                    }
                    catch (Exception loopEx) {
                        Log("壁纸切换时出现错误：" + loopEx.Message);
                        Thread.Sleep(30000);
                    }
                }

            } catch (Exception ex) {
                Log("FATAL: " + ex.ToString());
            }
        }
    }
}