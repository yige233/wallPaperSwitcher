using System;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WallpaperSwitcher
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IShellItem { }
    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IShellItemArray { }
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDesktopWallpaper
    {
        void SetWallpaper(string monitorID, string wallpaper); string GetWallpaper(string monitorID); string GetMonitorDevicePathAt(uint monitorIndex); uint GetMonitorDevicePathCount(); void GetMonitorRECT(string monitorID, out Rect displayRect); void SetBackgroundColor(uint color); uint GetBackgroundColor(); void SetPosition(int position); int GetPosition();
        void SetSlideshow(IShellItemArray items); void GetSlideshow(out IShellItemArray items); void SetSlideshowOptions(int options, uint slideshowTick); void GetSlideshowOptions(out int options, out uint slideshowTick); void AdvanceSlideshow(string monitorID, int direction); void GetStatus(out int state); bool Enable(bool enable);
    }
    public class WallpaperEngine
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
        [DllImport("shell32.dll", PreserveSig = true)]
        public static extern int SHCreateShellItemArrayFromShellItem([MarshalAs(UnmanagedType.Interface)] IShellItem psi, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppsiItemArray);
        public static void SetFolder(string path)
        {
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
    public class LockScreen
    {
        public static void SetWallpaper(string imagePath)
        {
            Exception threadEx = null;
            // 启动 STA 线程 (WinRT API 的硬性要求)
            Thread t = new Thread(() =>
            {
                try { InternalSet(imagePath); }
                catch (Exception ex) { threadEx = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (threadEx != null) throw threadEx;
        }

        private static void InternalSet(string path)
        {
            // --- 步骤 1: 自动查找 System.Runtime.WindowsRuntime.dll ---
            string runtimeDllPath = FindRuntimeDll();

            // --- 步骤 2: 动态加载运行时 DLL ---
            Assembly runtimeAssembly = Assembly.LoadFrom(runtimeDllPath);
            if (runtimeAssembly == null) throw new Exception("无法加载运行时 DLL。");

            // --- 步骤 3: 通过反射获取流转换方法 ---
            // 目标: System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(Stream)
            Type extType = runtimeAssembly.GetType("System.IO.WindowsRuntimeStreamExtensions");
            if (extType == null) throw new Exception("无法找到 WindowsRuntimeStreamExtensions 类型。");

            MethodInfo asRandomAccessStreamMethod = extType.GetMethod("AsRandomAccessStream",
                new Type[] { typeof(Stream) });

            if (asRandomAccessStreamMethod == null) throw new Exception("无法找到 AsRandomAccessStream 方法。");

            // --- 步骤 4: 打开文件流并转换 ---
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                // 动态调用转换方法，返回 IRandomAccessStream (作为 object)
                object winRtStream = asRandomAccessStreamMethod.Invoke(null, new object[] { fs });
                if (winRtStream == null) throw new Exception("流转换失败，返回空值。");

                // --- 步骤 5: 加载 WinRT LockScreen 类 ---
                // 使用字符串加载，避免编译时需要 Windows.winmd
                var tLock = Type.GetType("Windows.System.UserProfile.LockScreen, Windows.System.UserProfile, ContentType=WindowsRuntime");
                if (tLock == null) throw new Exception("无法加载 Windows.System.UserProfile.LockScreen 类。");

                // --- 步骤 6: 调用 SetImageStreamAsync ---
                var asyncOp = tLock.InvokeMember("SetImageStreamAsync",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static,
                    null, null, new object[] { winRtStream });

                if (asyncOp == null) throw new Exception("SetImageStreamAsync 返回空值。");

                // --- 步骤 7: 等待完成 ---
                WaitForCompletion(asyncOp);
            }
        }

        private static string FindRuntimeDll()
        {
            // 获取当前 .NET 环境的目录 (例如 C:\Windows\Microsoft.NET\Framework64\v4.0.30319)
            string frameworkPath = RuntimeEnvironment.GetRuntimeDirectory();
            string dllPath = Path.Combine(frameworkPath, "System.Runtime.WindowsRuntime.dll");

            if (File.Exists(dllPath)) return dllPath;

            // 备用查找路径 (防止环境路径异常)
            string fallbackPath = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll";
            if (File.Exists(fallbackPath)) return fallbackPath;

            throw new FileNotFoundException("找不到 System.Runtime.WindowsRuntime.dll，请确认安装了 .NET Framework 4.5+");
        }

        private static void WaitForCompletion(object asyncOp)
        {
            // 强制转换为本地定义的 IAsyncInfo 接口
            IAsyncInfo info = (IAsyncInfo)asyncOp;
            // 0 = Started
            while (info.Status == 0) { Thread.Sleep(100); }
            // 3 = Error
            if (info.Status == 3) { throw new Exception("设置锁屏操作失败。错误代码: " + info.ErrorCode); }
        }
        // --- 手动定义 COM 接口 (欺骗编译器，绕过 WinMD) ---
        [ComImport]
        [Guid("00000036-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        private interface IAsyncInfo
        {
            uint Id { get; }
            uint Status { get; }
            int ErrorCode { get; }
            void Cancel();
            void Close();
        }
    };
    partial class Program
    {
        static bool PrepareWallpaper(string targetDir, string url)
        {
            string p1 = Path.Combine(targetDir, "wp1.jpg");
            string p2 = Path.Combine(targetDir, "wp2.jpg");
            try { File.Delete(p1); } catch { }
            try { File.Delete(p2); } catch { }

            long jpgQuality = 95L;
            long.TryParse(ReadIni("JPGQuality", "95"), out jpgQuality);
            if (jpgQuality < 0) jpgQuality = 0;
            if (jpgQuality > 100) jpgQuality = 100;

            try
            {
                if (!Directory.Exists(targetDir)) { Directory.CreateDirectory(targetDir); }
                if (!DownloadImage(url, p1, jpgQuality)) { return false; }
                if (!Win32.CreateSymbolicLink(p2, p1, Win32.SYMBOLIC_LINK_FLAG_FILE | Win32.SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE))
                {
                    if (!Win32.CreateHardLink(p2, p1, IntPtr.Zero))
                    {
                        try { File.Copy(p1, p2, true); }
                        catch (Exception ex) { Log("无法创建 wp1.jpg 的备份: " + ex.Message); }
                    }
                }
                return true;
            }
            catch (Exception ex)
            { Log("准备壁纸失败: " + ex.Message); return false; }
        }

    }

}