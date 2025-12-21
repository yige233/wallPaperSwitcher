<#
.SYNOPSIS
    WallpaperApp 维护工具脚本
.DESCRIPTION
    用于管理锁屏自动切换服务的安装、卸载、启动以及创建快捷方式。
#>

# ==========================================
# 1. 自动提权检测
# ==========================================
$CurrentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$Principal = [Security.Principal.WindowsPrincipal]$CurrentIdentity
if (-not $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "正在请求管理员权限以执行维护操作..." -ForegroundColor Yellow
    Start-Process powershell.exe "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

# ==========================================
# 2. 配置变量
# ==========================================
$WorkDir = $PSScriptRoot
$ExeName = "WallpaperApp.exe"
$ExePath = Join-Path $WorkDir $ExeName
$TaskName = "WallpaperSwitcher"
$ShortcutName = "下一张壁纸.lnk"

# 检查可执行文件是否存在
if (-not (Test-Path $ExePath)) {
    Write-Host "错误: 在当前目录下找不到 $ExeName" -ForegroundColor Red
    Write-Host "请确保本脚本与程序在同一文件夹内。"
    Read-Host "按回车键退出..."
    exit
}

# ==========================================
# 3. 功能函数
# ==========================================

function Install-ServiceTask {
    Write-Host "`n正在创建计划任务..." -ForegroundColor Cyan
    try {
        # 定义操作：启动 EXE (无参数)
        $Action = New-ScheduledTaskAction -Execute $ExePath -WorkingDirectory $WorkDir
        
        # 定义触发器：用户登录时
        $Trigger = New-ScheduledTaskTrigger -AtLogon
        
        # 必须是当前用户，否则无法修改该用户的锁屏配置
        $Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive
        
        # 定义设置：允许使用电池启动，不因为运行时间过长而停止
        $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0
        
        # 注册任务 (如果存在则覆盖)
        Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings -Force | Out-Null
        
        Write-Host "√ 计划任务 [$TaskName] 创建成功！" -ForegroundColor Green
        Write-Host "   程序将在您每次登录时自动后台运行。"
    }
    catch {
        Write-Error "创建失败: $_"
    }
}

function Remove-ServiceTask {
    Write-Host "`n正在删除计划任务..." -ForegroundColor Cyan
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        try {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
            Write-Host "√ 计划任务已删除。" -ForegroundColor Green
            
            # 顺便尝试停止正在运行的进程
            $Proc = Get-Process -Name "WallpaperApp" -ErrorAction SilentlyContinue
            if ($Proc) {
                Stop-Process -InputObject $Proc -Force
                Write-Host "   已停止正在运行的实例。" -ForegroundColor Gray
            }
        }
        catch {
            Write-Error "删除失败: $_"
        }
    }
    else {
        Write-Host "× 未找到名为 [$TaskName] 的任务。" -ForegroundColor Yellow
    }
}

function Start-ServiceTask {
    Write-Host "`n正在启动计划任务..." -ForegroundColor Cyan
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        try {
            Start-ScheduledTask -TaskName $TaskName
            Write-Host "√ 任务已触发启动。" -ForegroundColor Green
        }
        catch {
            Write-Error "启动失败: $_"
        }
    }
    else {
        Write-Host "× 任务不存在，请先执行安装操作。" -ForegroundColor Red
    }
}

function New-Shortcut {
    Write-Host "`n正在创建桌面快捷方式..." -ForegroundColor Cyan
    try {
        $WshShell = New-Object -ComObject WScript.Shell
        $DesktopPath = [Environment]::GetFolderPath("Desktop")
        $LnkPath = Join-Path $DesktopPath $ShortcutName
        
        $Shortcut = $WshShell.CreateShortcut($LnkPath)
        $Shortcut.TargetPath = $ExePath
        $Shortcut.Arguments = "-s"  # 关键：添加 -s 参数用于手动切换
        $Shortcut.WorkingDirectory = $WorkDir
        $Shortcut.WindowStyle = 7   # 7 = 最小化 (Minimize)，避免弹窗闪烁
        $Shortcut.IconLocation = $ExePath # 使用 EXE 自身的图标
        $Shortcut.Description = "切换到下一张壁纸"
        $Shortcut.Save()
        
        Write-Host "√ 快捷方式 [$ShortcutName] 已创建到桌面。" -ForegroundColor Green
    }
    catch {
        Write-Error "创建失败: $_"
    }
}

# ==========================================
# 4. 主菜单循环
# ==========================================

Clear-Host
while ($true) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "    WallpaperSwitcher - 管理面板"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "当前路径: $ExePath" -ForegroundColor Gray
    Write-Host ""
    Write-Host "1. 安装/修复 服务 (创建开机自启任务)"
    Write-Host "2. 启动 服务 (如果已安装)"
    Write-Host "3. 卸载 服务 (删除任务并停止进程)"
    Write-Host "4. 创建 `“下一张壁纸`” 桌面快捷方式"
    Write-Host "Q. 退出"
    Write-Host ""
    
    $Selection = Read-Host "请选择操作 [1-4, Q]"
    Clear-Host
    switch ($Selection) {
        "1" { Install-ServiceTask; Start-ServiceTask } # 安装后自动启动
        "2" { Start-ServiceTask }
        "3" { Remove-ServiceTask }
        "4" { New-Shortcut }
        "Q" { exit }
        "q" { exit }
        Default { Write-Host "无效输入，请重试。" -ForegroundColor Yellow }
    }
    
    Write-Host ""
}