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

                using (var triggerEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SwitchEventName))
                using (var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName))
                {
                    int Interval = 300;
                    WaitHandle[] handles = new WaitHandle[] { quitEvent, triggerEvent };
                    Log("=== 服务启动 ===");
                    while (true)
                    {
                        int.TryParse(Config.Read("IntervalSeconds"), out Interval);
                        if (Interval < 10) { Interval = 10; }
                        try
                        {
                            int UserIdleThresholdSeconds;
                            int.TryParse(Config.Read("IdleThresholdSeconds"), out UserIdleThresholdSeconds);

                            if (SystemUtils.IsUserBusy() || !SystemUtils.IsMonitorActive()) { Log("当前处于全屏/锁屏/显示器休眠状态，不更新壁纸。"); goto AwaitSignal; }
                            if (SystemUtils.IsUserIdle(UserIdleThresholdSeconds)) { Log("当前用户处于空闲状态，不更新壁纸。"); goto AwaitSignal; }

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
                        int index = WaitHandle.WaitAny(handles, Interval * 1000);
                        if (index == 0) { Log("收到了退出信号，程序已不再运行。"); ; break; }
                        if (index == 1) { Log("触发了手动壁纸切换。"); continue; }

                    }
                }
            }
            catch (Exception ex) { LogForce("后台服务运行时出现关键错误: {0}", ex.ToString()); }
        }
    }
}
