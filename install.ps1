<#
.SYNOPSIS
    工程化安装脚本
#>

param (
    [Parameter(Mandatory = $true)]
    [string]$ImageUrl,

    [int]$IntervalSeconds = 300,

    [string]$BasePath = "D:\Picture"
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$SourceFile = Join-Path $ScriptDir "Source.cs"
$ExePath = Join-Path $ScriptDir "WallpaperApp.exe"
$ConfigPath = Join-Path $ScriptDir "config.ini"

if (-not (Test-Path $SourceFile)) {
    Write-Error "找不到 Source.cs 文件！"
    exit
}

# 1. 生成 INI 配置 (Unicode)
$IniContent = @"
[Settings]
ImageUrl=$ImageUrl
IntervalSeconds=$IntervalSeconds
BasePath=$BasePath
Log=false
CurrentSlot=Slot_2
"@
$IniContent | Set-Content -Path $ConfigPath -Encoding Unicode
Write-Host "1. 配置文件已生成" -ForegroundColor Green

# 2. 编译 EXE
Write-Host "2. 正在编译 WallpaperApp.exe..." -ForegroundColor Yellow
Stop-Process -Name "WallpaperApp" -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
if (Test-Path $ExePath) { Remove-Item $ExePath -Force }

try {
    Add-Type -Path $SourceFile -OutputAssembly $ExePath -OutputType WindowsApplication -ReferencedAssemblies System.Drawing
    Write-Host "   编译成功" -ForegroundColor Green
}
catch { Write-Error "编译失败: $_"; exit }

# 3. 注册服务
Write-Host "3. 注册后台服务..." -ForegroundColor Cyan
$TaskName = "AutoWallpaperService"
$Action = New-ScheduledTaskAction -Execute $ExePath 
$Trigger = New-ScheduledTaskTrigger -AtLogon
$Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Days 365)
Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
try {
    Register-ScheduledTask -Action $Action -Trigger $Trigger -Settings $Settings -TaskName $TaskName -Description "自动壁纸服务 (WallpaperApp)" | Out-Null
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "   服务已启动" -ForegroundColor Green
}
catch { Write-Error "注册任务失败: $_" }

# 4. 创建快捷方式
try {
    $WshShell = New-Object -ComObject WScript.Shell
    $DesktopPath = [Environment]::GetFolderPath("Desktop")
    $ShortcutPath = Join-Path $DesktopPath "下一个壁纸.lnk"
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = $ExePath
    $Shortcut.Arguments = "-s" 
    $Shortcut.IconLocation = "shell32.dll,323"
    $Shortcut.Save()
    Write-Host "4. 桌面快捷方式已更新" -ForegroundColor Green
}
catch {}

Write-Host "`n>>> 全部完成 <<<" -ForegroundColor Green