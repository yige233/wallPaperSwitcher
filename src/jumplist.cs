using System;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Diagnostics;
using System.Collections.Generic;

namespace WallpaperSwitcher
{
    public static class JumpListInjector
    {
        public static void Run()
        {
            // 必须在 STA 线程中运行 WPF 逻辑
            Thread t = new Thread(() => RunInternal());
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }

        private static void RunInternal()
        {
            try
            {
                App.LogForce("正在加载 WPF 组件...");

                Assembly assemblyPF = Assembly.Load("PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                Assembly assemblyBase = Assembly.Load("WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");

                if (assemblyPF == null || assemblyBase == null)
                {
                    App.LogForce("错误: 无法加载 WPF 程序集。请确认已安装 .NET Framework。");
                    return;
                }

                Type tShutdownMode = assemblyPF.GetType("System.Windows.ShutdownMode");
                Type tApplication = assemblyPF.GetType("System.Windows.Application");
                Type tJumpList = assemblyPF.GetType("System.Windows.Shell.JumpList");
                Type tJumpTask = assemblyPF.GetType("System.Windows.Shell.JumpTask");
                Type tJumpItem = assemblyPF.GetType("System.Windows.Shell.JumpItem");

                if (tApplication == null || tJumpList == null || tJumpTask == null)
                {
                    App.LogForce("错误: 无法解析 WPF 类型。");
                    return;
                }

                object appInstance = tApplication.GetProperty("Current").GetValue(null, null);

                if (appInstance == null)
                {
                    appInstance = Activator.CreateInstance(tApplication);
                    object explicitShutdown = Enum.ToObject(tShutdownMode, 2);
                    tApplication.GetProperty("ShutdownMode").SetValue(appInstance, explicitShutdown, null);
                }

                object listInstance = Activator.CreateInstance(tJumpList);

                tJumpList.GetProperty("ShowFrequentCategory").SetValue(listInstance, false, null);
                tJumpList.GetProperty("ShowRecentCategory").SetValue(listInstance, false, null);

                object jumpItemsCollection = tJumpList.GetProperty("JumpItems").GetValue(listInstance, null);
                IList jumpItemsList = jumpItemsCollection as IList;

                var tasks = new List<object>
                {
                    CreateTask(tJumpTask, "启动/切换下一张壁纸", "立即更换锁屏壁纸", App.ExecutablePath),
                    CreateTask(tJumpTask, "编辑配置", "打开并编辑配置文件", App.GetConfigFilePath()),
                    CreateTask(tJumpTask, "打开日志", "打开日志文件", App.LogPath),
                    CreateTask(tJumpTask, "查看当前壁纸", "需要后台程序正在运行。", App.ExecutablePath, "--signal view"),
                    CreateTask(tJumpTask, "查看当前壁纸URL", "需要后台程序正在运行。", App.ExecutablePath, "--signal openURL"),
                    CreateTask(tJumpTask, "停止服务", "停止后台运行中的服务", App.ExecutablePath, "-q"),
                    CreateTask(tJumpTask, "关于", string.Format("关于{0}", App.AppName), App.GithubURL)
                };

                foreach (var task in tasks) { jumpItemsList.Add(task); }

                MethodInfo setJumpListMethod = tJumpList.GetMethod("SetJumpList", BindingFlags.Static | BindingFlags.Public);
                setJumpListMethod.Invoke(null, new object[] { appInstance, listInstance });

                tJumpList.GetMethod("Apply").Invoke(listInstance, null);

                App.LogForce("JumpList 更新成功。");
            }
            catch (Exception ex)
            {
                App.LogForce("JumpList 更新失败: {0}", ex.Message);
                if (ex.InnerException != null) App.LogForce(" - {0}", ex.InnerException.Message);
            }
        }

        /// <summary>
        /// 通过反射创建 JumpTask 对象
        /// </summary>
        /// <param name="tJumpTask">JumpTask类型</param>
        /// <param name="title">JumpTask标题</param>
        /// <param name="desc">JumpTask简介</param>
        /// <param name="appPath">JumpTask要执行的exe路径</param>
        /// <param name="args">传递给上述exe的命令参数</param>
        /// <param name="category">JumpTask菜单中的分组标题。如果是null，会被分到默认分组“任务”中，并且显示与否不再受系统设置影响。</param>
        /// <returns></returns>
        private static object CreateTask(Type tJumpTask, string title, string desc, string appPath, string args = null, string category = null)
        {
            object task = Activator.CreateInstance(tJumpTask);

            // 使用反射设置属性
            tJumpTask.GetProperty("Title").SetValue(task, title, null);
            tJumpTask.GetProperty("Description").SetValue(task, desc, null);
            tJumpTask.GetProperty("ApplicationPath").SetValue(task, appPath, null);
            tJumpTask.GetProperty("Arguments").SetValue(task, args, null);
            tJumpTask.GetProperty("IconResourcePath").SetValue(task, appPath, null); // 默认用 exe 图标
            tJumpTask.GetProperty("IconResourceIndex").SetValue(task, 0, null);
            tJumpTask.GetProperty("CustomCategory").SetValue(task, category, null);

            return task;
        }
    }
}