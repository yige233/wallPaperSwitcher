# 定义子路径 (使用 Join-Path 前确保 ScriptRoot 不为空，虽然上面已经保证了)
$SourceDir = Join-Path $PSScriptRoot "src"
$IconPath = Join-Path $PSScriptRoot "icon.ico"
$OutputPath = Join-Path $PSScriptRoot "WallpaperApp.exe"

if (-not (Test-Path $SourceDir)) {
    Write-Error "严重错误: 找不到源码目录"
    Write-Error "期待路径: $SourceDir"
    Write-Error "请确保你在项目根目录下，并且创建了 'src' 文件夹。"
    exit 1
}

Stop-Process -Name "WallpaperApp" -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force }


# 获取所有 .cs 文件
$SourceFiles = @(Get-ChildItem -Path $SourceDir -Filter *.cs | Select-Object -ExpandProperty FullName)

if ($SourceFiles.Count -eq 0) {
    Write-Error "错误: 在 $SourceDir 下没找到任何 .cs 文件。"
    exit 1
}

Write-Host "检测到 $($SourceFiles.Count) 个源码文件，准备编译..." -ForegroundColor Cyan

$CodeProvider = New-Object Microsoft.CSharp.CSharpCodeProvider
$Params = New-Object System.CodeDom.Compiler.CompilerParameters

# 生成 EXE
$Params.GenerateExecutable = $true
$Params.OutputAssembly = $OutputPath

# 添加引用 (静默添加)
[void]$Params.ReferencedAssemblies.Add("System.dll")
[void]$Params.ReferencedAssemblies.Add("System.Core.dll")
[void]$Params.ReferencedAssemblies.Add("System.Drawing.dll")
[void]$Params.ReferencedAssemblies.Add("System.Net.Http.dll")
[void]$Params.ReferencedAssemblies.Add("System.Windows.Forms.dll")

# 编译参数
# /target:winexe : 无控制台窗口
# /optimize+     : 优化代码
$CompilerOptions = "/target:winexe /optimize+"

if (Test-Path $IconPath) {
    Write-Host "集成图标: $IconPath" -ForegroundColor Gray
    $CompilerOptions += " /win32icon:`"$IconPath`""
}

$Params.CompilerOptions = $CompilerOptions

try {
    # 强制转换文件列表为 string[]，防止类型绑定错误
    $Results = $CodeProvider.CompileAssemblyFromFile($Params, [string[]]$SourceFiles)

    if ($Results.Errors.HasErrors) {
        Write-Error "`n编译失败！详情如下："
        foreach ($Err in $Results.Errors) {
            $FileName = Split-Path $Err.FileName -Leaf
            $Msg = "[{0}:{1}] {2}" -f $FileName, $Err.Line, $Err.ErrorText
            
            if ($Err.IsWarning) {
                Write-Warning $Msg
            }
            else {
                Write-Error $Msg
            }
        }
    }
    else {
        Write-Host "`n================================"
        Write-Host "    编译成功！" -ForegroundColor Green
        Write-Host "================================"
        Write-Host "输出文件: $OutputPath"
    }
}
catch {
    Write-Error "编译器内部错误: $_"
}
finally {
    Read-Host -Prompt "按Enter键退出"
}