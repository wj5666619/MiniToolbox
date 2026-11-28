# 小工具箱构建脚本（源码按顶层类型拆到 Src\，必须多文件编译）
# 用法：右键“使用 PowerShell 运行”，或在 PowerShell 里执行  .\build.ps1
# 可选参数：-NoSign  跳过签名；-NoPack  跳过 7z 打包

param(
    [switch]$NoSign,
    [switch]$NoPack
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$ver = (Select-String -LiteralPath (Join-Path $root 'Src\UiKit.cs') -Pattern 'VER = "([\d.]+)"' |
        Select-Object -First 1).Matches[0].Groups[1].Value
Write-Host "构建版本 v$ver" -ForegroundColor Cyan

# 1) 关掉正在运行的实例，否则 exe 被占用
Get-Process 小工具箱 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# 2) 编译（主文件 + Src 下全部 .cs）
$f = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$cscArgs = @(
    '/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
    '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',
    '/r:System.Web.Extensions.dll', '/r:Accessibility.dll',
    "/r:$f\System.Management.dll", "/r:$f\WPF\UIAutomationClient.dll",
    "/r:$f\WPF\UIAutomationTypes.dll", "/r:$f\WPF\WindowsBase.dll",
    "/r:$f\WPF\System.Speech.dll", '/r:System.Security.dll', '/r:Microsoft.VisualBasic.dll',
    "/r:$root\refs\WV2Core.dll", "/r:$root\refs\WV2WinForms.dll",
    "/win32icon:$root\定时关机.ico",
    "/resource:$root\sam_tool.zip,SamZip",
    "/resource:$root\clean_tool.zip,CleanZip",
    "/resource:$root\refs\WV2Core.dll,WV2Core",
    "/resource:$root\refs\WV2WinForms.dll,WV2WinForms",
    "/resource:$root\refs\WV2Loader32.dll,WV2Loader32",
    "/resource:$root\refs\WV2Loader64.dll,WV2Loader64",
    "/out:$root\小工具箱_new.exe",
    "$root\定时关机.cs", "$root\Src\*.cs"
)
& "$f\csc.exe" @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

Move-Item -Force "$root\小工具箱_new.exe" "$root\小工具箱.exe"
Write-Host "编译完成：$((Get-Item "$root\小工具箱.exe").Length) 字节" -ForegroundColor Green

# 3) 签名（自签名证书，按主题查找；证书不在就跳过并提示）
if (-not $NoSign) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like '*MiniToolbox*' } | Select-Object -First 1
    if ($cert) {
        $st = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe'
        if (Test-Path $st) {
            & $st sign /sha1 $cert.Thumbprint /fd SHA256 "$root\小工具箱.exe" | Out-Null
            $sig = Get-AuthenticodeSignature "$root\小工具箱.exe"
            Write-Host "签名：$($sig.Status)" -ForegroundColor Green
        } else {
            Write-Host "未找到 signtool，跳过签名" -ForegroundColor Yellow
        }
    } else {
        Write-Host "未找到代码签名证书，跳过签名（新证书：New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=MiniToolbox (wj5666619)' -CertStoreLocation Cert:\CurrentUser\My）" -ForegroundColor Yellow
    }
}

# 4) 更新清单：版本、大小、sha256（签名之后再算，顺序不能反）
$exe = Get-Item "$root\小工具箱.exe"
$hash = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.ToLower()
$jsonPath = Join-Path $root '发布\update.json'
if (-not (Test-Path -LiteralPath $jsonPath)) { $jsonPath = Join-Path $root 'update.json' }
$old = Get-Content $jsonPath -Raw | ConvertFrom-Json
$o = [ordered]@{
    version = $ver
    size    = $exe.Length
    sha256  = $hash
    url     = 'https://github.com/wj5666619/MiniToolbox/releases/latest/download/MiniToolbox.exe'
    notes   = $old.notes
}
[IO.File]::WriteAllText($jsonPath, ($o | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
Write-Host "清单已更新：v$ver / $($exe.Length) 字节 / sha256 $hash" -ForegroundColor Green

# 5) 打包（exe + 皮肤.txt）
if (-not $NoPack) {
    $seven = "$env:TEMP\7zr.exe"
    if (Test-Path $seven) {
        $pkg = Join-Path $root "小工具箱_v$ver.7z"
        if (Test-Path $pkg) { Remove-Item $pkg -Force }
        & $seven a -t7z -mx9 $pkg "$root\小工具箱.exe" "$root\皮肤.txt" | Out-Null
        Write-Host "打包：$pkg（$((Get-Item $pkg).Length) 字节）" -ForegroundColor Green
    } else {
        Write-Host "未找到 7zr.exe（$seven），跳过打包" -ForegroundColor Yellow
    }
}

Write-Host "完成。上传 GitHub 时：update.json 覆盖 main 分支，exe 作为 Release 资产，资产名 MiniToolbox.exe" -ForegroundColor Cyan
