param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Output = "sbom.cdx.json",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDir)) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $RepoRoot = Split-Path -Parent $scriptDir
}

function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-Hash([string]$Path) {
    $h = Get-Sha256 $Path
    if (-not $h) { return @() }
    return @(@{ alg = 'SHA-256'; content = $h })
}

$exePath = (Resolve-Path -LiteralPath $Exe).Path
$appHash = Get-Sha256 $exePath
$appRef = "pkg:generic/minitoolbox@$Version"

$components = @()

$refs = @(
    @{ name = 'WV2Core'; file = 'refs/WV2Core.dll'; license = 'Microsoft WebView2 SDK license' },
    @{ name = 'WV2WinForms'; file = 'refs/WV2WinForms.dll'; license = 'Microsoft WebView2 SDK license' },
    @{ name = 'WV2Loader32'; file = 'refs/WV2Loader32.dll'; license = 'Microsoft WebView2 SDK license' },
    @{ name = 'WV2Loader64'; file = 'refs/WV2Loader64.dll'; license = 'Microsoft WebView2 SDK license' }
)

foreach ($r in $refs) {
    $p = Join-Path $RepoRoot $r.file
    if (-not (Test-Path -LiteralPath $p)) { continue }
    $components += [ordered]@{
        type       = 'library'
        'bom-ref'  = "pkg:generic/$($r.name.ToLowerInvariant())"
        name       = $r.name
        version    = 'embedded'
        hashes     = New-Hash $p
        licenses   = @(@{ license = @{ name = $r.license } })
        properties = @(@{ name = 'minitoolbox:form'; value = 'embedded-resource' })
    }
}

$archives = @(
    @{ name = 'ToolSite'; file = 'docs/ToolSite.zip'; license = 'MIT (upstream toolsite)' },
    @{ name = 'FFmpeg'; file = 'docs/ffmpeg_tool.zip'; license = 'LGPL/GPL (upstream build)' },
    @{ name = 'SAM'; file = 'sam_tool.zip'; license = 'Upstream project license' },
    @{ name = 'CleanTool'; file = 'clean_tool.zip'; license = 'Upstream project license' },
    @{ name = 'VideoTool'; file = 'docs/video_tool.zip'; license = 'Upstream project license' }
)

foreach ($a in $archives) {
    $p = Join-Path $RepoRoot $a.file
    if (-not (Test-Path -LiteralPath $p)) { continue }
    $components += [ordered]@{
        type      = 'file'
        'bom-ref' = "pkg:generic/$($a.name.ToLowerInvariant())"
        name      = $a.name
        version   = 'optional-payload'
        hashes    = New-Hash $p
        licenses  = @(@{ license = @{ name = $a.license } })
        properties = @(@{ name = 'minitoolbox:form'; value = 'optional-download' })
    }
}

$bom = [ordered]@{
    bomFormat   = 'CycloneDX'
    specVersion = '1.5'
    serialNumber = "urn:uuid:$([guid]::NewGuid().ToString())"
    version     = 1
    metadata    = [ordered]@{
        timestamp = (Get-Date).ToUniversalTime().ToString('o')
        tools     = @(@{ vendor = 'MiniToolbox'; name = 'New-Sbom.ps1'; version = '1.0' })
        component = [ordered]@{
            type       = 'application'
            'bom-ref'  = $appRef
            name       = 'MiniToolbox'
            version    = $Version
            hashes     = @(@{ alg = 'SHA-256'; content = $appHash })
            licenses   = @(@{ license = @{ id = 'MIT' } })
            externalReferences = @(@{ type = 'distribution'; url = 'https://github.com/wj5666619/MiniToolbox/releases/latest' })
        }
    }
    components   = $components
    dependencies = @(@{ ref = $appRef; dependsOn = @($components | ForEach-Object { $_.'bom-ref' }) })
}

$json = $bom | ConvertTo-Json -Depth 12
$outPath = [IO.Path]::GetFullPath($Output)
[IO.File]::WriteAllText($outPath, $json, (New-Object Text.UTF8Encoding($false)))
Write-Host "SBOM: $outPath ($($components.Count) components, app=$appHash)"
