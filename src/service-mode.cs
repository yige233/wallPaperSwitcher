using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;

namespace WallpaperSwitcher
{
    partial class App
    {
        public static string currentWallpaperURL;
        public static void RunServiceMode()
        {
            try
            {
                // 确保关键配置项存在
                string BasePathRaw = Config.Read("BasePath");
                if (string.IsNullOrEmpty(BasePathRaw))
                {
                    MessageBox.Show("配置文件中需要提供BasePath。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Stop);
                    Process.Start(ConfigPath);
                    return;
                }

                // --- 1. 基础路径定义 ---
                string BasePath = Path.GetFullPath(BasePathRaw);
                string Slot1 = Path.Combine(BasePath, "Slot_1");
                string Slot2 = Path.Combine(BasePath, "Slot_2");
                Directory.CreateDirectory(Slot1);
                Directory.CreateDirectory(Slot2);
                // --- 2. 状态初始化 ---
                string CurrentSlotName = Config.Read("CurrentSlot");
                string NextSlotName = (CurrentSlotName == "Slot_1") ? "Slot_2" : "Slot_1";

                int Interval = 300;
                var handles = new WaitHandle[] { SignalManager.GetHandle("quit"), SignalManager.GetHandle("switch") };
                Log("=== 服务启动 ===");
                while (true)
                {
                    int.TryParse(Config.Read("IntervalSeconds"), out Interval);
                    try
                    {
                        int UserIdleThresholdSeconds;
                        int.TryParse(Config.Read("IdleThresholdSeconds"), out UserIdleThresholdSeconds);

                        Func<string> TryGetReason = delegate ()
                        {
                            if (Utils.IsUserBusy()) { return "全屏"; }
                            if (Utils.IsLocked()) { return "锁屏"; }
                            if (Utils.IsUserIdle(UserIdleThresholdSeconds)) { return "用户空闲"; }
                            // “处于远程桌面”和“显示器处于活动状态”都不成立，我们认为用户连接到了本地会话，且显示器处于关闭状态，那么放弃更新壁纸。
                            if (!(Utils.IsRDPSession() || Utils.IsMonitorActivated())) { return "显示器休眠"; }
                            return null;
                        };
                        string skipReason = TryGetReason();
                        if (skipReason != null) { Log("当前处于 {0} 状态，不更新壁纸。", skipReason); goto AwaitSignal; }

                        string targetPath = (NextSlotName == "Slot_1") ? Slot1 : Slot2;
                        // 检查目标文件夹是否为空，若为空，则立即准备壁纸，若准备壁纸失败，则放弃后续操作。
                        if (Directory.GetFiles(targetPath, "*.jpg").Length == 0)
                        {
                            if (!PrepareWallpaper(targetPath))
                            {
                                int retryAfter;
                                int.TryParse(Config.Read("RetryAfter"), out retryAfter);
                                Interval = retryAfter;
                                Log("{0} 未就绪，放弃切换壁纸。预计将在 {1} 秒后重试。", NextSlotName, retryAfter);
                                goto AwaitSignal;
                            }
                        }
                        // 执行切换
                        WallpaperEngine.SetFolder(targetPath);
                        currentWallpaperURL = Downloader.LastURL;
                        // 设置锁屏壁纸
                        bool enableLockScreen;
                        string WallpaperPath = Path.Combine(targetPath, "wp1.jpg");
                        bool.TryParse(Config.Read("LockScreen"), out enableLockScreen);
                        if (enableLockScreen)
                        {
                            if (!File.Exists(WallpaperPath)) { LogForce("文件不存在: {0} ，无法将其设置为锁屏壁纸。", WallpaperPath); }
                            if (Image.IsExtraFormat(Image.GetImageFormat(WallpaperPath))) { LogForce("{0} 为webp格式图片，无法将其设置为锁屏壁纸。", WallpaperPath); }
                            else { LockScreen.SetWallpaper(WallpaperPath); }
                        }
                        // 交换变量
                        string temp = CurrentSlotName;
                        CurrentSlotName = NextSlotName;
                        NextSlotName = temp;

                        Config.Write("CurrentSlot", CurrentSlotName);
                        if (!PrepareWallpaper((NextSlotName == "Slot_1") ? Slot1 : Slot2)) { Log("后台预加载 {0} 未完成。将在下个周期重试。", NextSlotName); }

                    }
                    catch (Exception loopEx) { LogForce("壁纸切换时出现错误: {0}", loopEx.Message); goto AwaitSignal; }

                AwaitSignal:
                    if (Interval < 10) { break; }
                    int index = WaitHandle.WaitAny(handles, Interval * 1000);
                    if (index == 0) { Log("收到了退出信号，程序已不再运行。"); ; break; }
                    if (index == 1) { Log("触发了手动壁纸切换。"); continue; }
                }
            }
            catch (Exception ex) { LogForce("后台服务运行时出现关键错误: {0}", ex.ToString()); }
        }
    }
}
