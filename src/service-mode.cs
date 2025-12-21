using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;

namespace WallpaperSwitcher
{
    partial class Program
    {
        static Mutex appMutex;
        public static void RunServiceMode()
        {
            try
            {
                // 确保只有一个实例运行，以及配置文件存在
                bool createdNew;
                appMutex = new Mutex(true, "Global\\" + AppName + "ServiceMutex", out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show("已经存在一个正在运行的" + AppName + "。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!File.Exists(ConfigPath)) { InitConfig(); return; }

                // 确保关键配置项存在
                string BasePathRaw = ReadIni("BasePath", "");
                string ImageUrl = ReadIni("ImageUrl", "");
                if (string.IsNullOrEmpty(BasePathRaw) || string.IsNullOrEmpty(ImageUrl)) { MessageBox.Show("需要提供BasePath和ImageUrl。" + ConfigPath, AppName, MessageBoxButtons.OK, MessageBoxIcon.Stop); return; }

                // --- 1. 基础路径定义 ---
                string BasePath = Path.GetFullPath(BasePathRaw);
                string Slot1 = Path.Combine(BasePath, "Slot_1");
                string Slot2 = Path.Combine(BasePath, "Slot_2");
                Directory.CreateDirectory(Slot1);
                Directory.CreateDirectory(Slot2);
                // --- 2. 状态初始化 ---
                string CurrentSlotName = ReadIni("CurrentSlot", "Slot_1");
                string NextSlotName = (CurrentSlotName == "Slot_1") ? "Slot_2" : "Slot_1";

                using (var triggerEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SwitchEventName))
                using (var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName))
                {
                    bool enableLockScreen = true;
                    int Interval = 300;
                    WaitHandle[] handles = new WaitHandle[] { quitEvent, triggerEvent };
                    Log("=== 服务启动 ===");
                    while (true)
                    {

                        try
                        {
                            int UserIdleThresholdSeconds = 900;
                            int.TryParse(ReadIni("IdleThresholdSeconds", "900"), out UserIdleThresholdSeconds);

                            if (SystemUtils.IsUserBusy() || !SystemUtils.IsMonitorActive()) { Log("当前处于全屏/锁屏/显示器休眠状态，不更新壁纸。"); goto AwaitSignal; }
                            if (SystemUtils.IsUserIdle(UserIdleThresholdSeconds)) { Log("当前用户处于空闲状态，不更新壁纸。"); goto AwaitSignal; }

                            string targetPath = (NextSlotName == "Slot_1") ? Slot1 : Slot2;
                            // 检查目标文件夹是否为空，若为空，则立即准备壁纸，若准备壁纸失败，则放弃后续操作。
                            if (Directory.GetFiles(targetPath, "*.jpg").Length == 0)
                            {
                                if (!PrepareWallpaper(targetPath, ImageUrl))
                                {
                                    Log(NextSlotName + " 未就绪，放弃切换壁纸。将在下一个周期重试。");
                                    goto AwaitSignal;
                                }
                            }
                            // 执行切换
                            WallpaperEngine.SetFolder(targetPath);
                            // 设置锁屏壁纸
                            string WallpaperPath = Path.Combine(targetPath, "wp1.jpg");
                            bool.TryParse(ReadIni("LockScreen", "true"), out enableLockScreen);
                            if (enableLockScreen)
                            {
                                if (!File.Exists(WallpaperPath)) { Log("文件不存在：" + WallpaperPath + " ，无法将其设置为锁屏壁纸。"); }
                                else { LockScreen.SetWallpaper(WallpaperPath); }
                            }
                            // 交换变量
                            string temp = CurrentSlotName;
                            CurrentSlotName = NextSlotName;
                            NextSlotName = temp;

                            WriteIni("CurrentSlot", CurrentSlotName);
                            if (!PrepareWallpaper((NextSlotName == "Slot_1") ? Slot1 : Slot2, ImageUrl)) { Log("后台预加载 " + NextSlotName + " 未完成。将在下个周期重试。"); }

                        }
                        catch (Exception loopEx) { Log("壁纸切换时出现错误：" + loopEx.Message); goto AwaitSignal; }

                    AwaitSignal:

                        int.TryParse(ReadIni("IntervalSeconds", "300"), out Interval);
                        int index = WaitHandle.WaitAny(handles, Interval * 1000);
                        if (index == 0) { Log("收到了退出信号，程序已不再运行。"); ; break; }
                        if (index == 1) { Log("触发了手动壁纸切换。"); continue; }

                    }
                }
            }
            catch (Exception ex) { Log("FATAL: " + ex.ToString()); }
        }
    }
}
