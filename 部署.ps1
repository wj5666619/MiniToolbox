<#
  小工具箱 · 企业部署脚本（v19.9）
  用法（Windows PowerShell 5.1 及以上，无需管理员）：
    .\部署.ps1                  # 安装到当前用户 %LOCALAPPDATA%\MiniToolbox
    .\部署.ps1 -Install        # 同上
    .\部署.ps1 -Install -AutoStart   # 同时加入开机自启
    .\部署.ps1 -Uninstall      # 卸载（保留日志与配置，可选 -Purge 一并删除）
    .\部署.ps1 -Status         # 查看安装状态
    .\部署.ps1 -MachinePolicy # 生成机器级策略模板到 %ProgramData%\MiniToolbox\策略.json（需管理员）
#>
param(
    [switch]$Install,
    [switch]$Uninstall,
    [switch]$Status,
    [switch]$AutoStart,
    [switch]$Purge,
    [switch]$MachinePolicy,
    [string]$Source = ""
)

$ErrorActionPreference = 'Stop'
$AppName = '小工具箱'
$InstallDir = Join-Path $env:LOCALAPPDATA 'MiniToolbox'
$ExePath = Join-Path $InstallDir '小工具箱.exe'
$ScriptPath = $MyInvocation.MyCommand.Path
$RegKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MiniToolbox'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$StartDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\MiniToolbox'
$LinkPath = Join-Path $StartDir '小工具箱.lnk'

function Get-VersionOf([string]$p) {
    if (-not (Test-Path $p)) { return '' }
    try { (Get-Item $p).VersionInfo.ProductVersion } catch { '' }
}

function Make-Shortcut([string]$target) {
    if (-not (Test-Path $StartDir)) { New-Item -ItemType Directory -Path $StartDir -Force | Out-Null }
    $ws = New-Object -ComObject WScript.Shell
    $ln = $ws.CreateShortcut($LinkPath)
    $ln.TargetPath = $target
    $ln.WorkingDirectory = $InstallDir
    $ln.Description = '小工具箱 · 单文件 Windows 工具集'
    $ln.Save()
}

function Do-Install {
    if (-not $Source) {
        $cand = Join-Path $PSScriptRoot '小工具箱.exe'
        if (-not (Test-Path $cand)) { $cand = Join-Path $PSScriptRoot 'MiniToolbox.exe' }
        if (-not (Test-Path $cand)) { throw '未找到 小工具箱.exe，请把它与本脚本放在同一目录，或用 -Source 指定路径。' }
        $Source = $cand
    }
    if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }

    Get-Process -Name 小工具箱 -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    Copy-Item -LiteralPath $Source -Destination $ExePath -Force
    $ver = Get-VersionOf $ExePath

    Make-Shortcut $ExePath

    if (-not (Test-Path $RegKey)) { New-Item -Path $RegKey -Force | Out-Null }
    Set-ItemProperty -Path $RegKey -Name 'DisplayName' -Value $AppName
    Set-ItemProperty -Path $RegKey -Name 'DisplayVersion' -Value $ver
    Set-ItemProperty -Path $RegKey -Name 'Publisher' -Value 'MiniToolbox'
    Set-ItemProperty -Path $RegKey -Name 'DisplayIcon' -Value ($ExePath + ',0')
    Set-ItemProperty -Path $RegKey -Name 'InstallLocation' -Value $InstallDir
    Set-ItemProperty -Path $RegKey -Name 'UninstallString' -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $ScriptPath + '" -Uninstall')
    Set-ItemProperty -Path $RegKey -Name 'EstimatedSize' -Value ([int]((Get-Item $ExePath).Length / 1024))

    if ($AutoStart) {
        if (-not (Test-Path $RunKey)) { New-Item -Path $RunKey -Force | Out-Null }
        Set-ItemProperty -Path $RunKey -Name 'MiniToolbox' -Value ('"' + $ExePath + '" /nosplash')
    } else {
        Remove-ItemProperty -Path $RunKey -Name 'MiniToolbox' -ErrorAction SilentlyContinue
    }

    Write-Host ('已安装：' + $ExePath + '  版本 ' + $ver)
    Write-Host ('开始菜单：' + $LinkPath)
    if ($AutoStart) { Write-Host '已加入开机自启（可在设置里关掉）' }
}

function Do-Uninstall {
    Get-Process -Name 小工具箱 -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-ItemProperty -Path $RunKey -Name 'MiniToolbox' -ErrorAction SilentlyContinue
    if (Test-Path $LinkPath) { Remove-Item -LiteralPath $LinkPath -Force }
    if (Test-Path $StartDir) {
        $left = Get-ChildItem -LiteralPath $StartDir -Force
        if ($left.Count -eq 0) { Remove-Item -LiteralPath $StartDir -Force }
    }
    if (Test-Path $RegKey) { Remove-Item -Path $RegKey -Force }
    if ($Purge) {
        if (Test-Path $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
        Write-Host '已卸载并清除程序目录（日志与配置一并删除）。'
    } else {
        Get-ChildItem -LiteralPath $InstallDir -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne '日志' -and $_.Name -ne '配置.json' -and $_.Name -ne '配置.json.bak' } |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host '已卸载（日志与配置保留在程序目录，可加 -Purge 一并删除）。'
    }
}

function Do-Status {
    $exists = Test-Path $ExePath
    Write-Host ('程序：' + $(if ($exists) { $ExePath } else { '未安装' }))
    if ($exists) { Write-Host ('版本：' + (Get-VersionOf $ExePath)) }
    Write-Host ('开始菜单：' + $(if (Test-Path $LinkPath) { $LinkPath } else { '无' }))
    $run = (Get-ItemProperty -Path $RunKey -Name 'MiniToolbox' -ErrorAction SilentlyContinue).MiniToolbox
    Write-Host ('开机自启：' + $(if ($run) { $run } else { '未启用' }))
    Write-Host ('注册卸载项：' + $(if (Test-Path $RegKey) { '有' } else { '无' }))
}

function Do-MachinePolicy {
    $dir = Join-Path (Join-Path $env:ProgramData 'MiniToolbox') ''
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $p = Join-Path $dir '策略.json'
    if (Test-Path $p) { Write-Host ('已存在：' + $p + '（未覆盖）'); return }
    @'
{
  "禁用在线更新": false,
  "禁用诊断导出": false,
  "详细日志": false,
  "禁用功能": []
}
'@ | Set-Content -LiteralPath $p -Encoding UTF8
    Write-Host ('已生成策略模板：' + $p)
    Write-Host '禁用功能可填槽位名或中文页名：video / music / sam / clean / wm / val / sys / 重装'
}

if ($MachinePolicy) { Do-MachinePolicy; return }
if ($Uninstall) { Do-Uninstall; return }
if ($Status) { Do-Status; return }
Do-Install
