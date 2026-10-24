<#
.SYNOPSIS
    安装 CAD 输入法自动切换插件（按当前用户安装，无需管理员权限）。

.DESCRIPTION
    1. 将 CadImeSwitch.bundle 复制到 %APPDATA%\Autodesk\ApplicationPlugins
    2. 把插件目录追加到当前用户所有 AutoCAD 配置的 TRUSTEDPATHS
    3. 首次启动 CAD 时自动加载；配置保存在 %APPDATA%\CadImeSwitch\config.json

.PARAMETER BundleSource
    可选。指定 CadImeSwitch.bundle 目录；默认使用脚本旁边的 bundle。

.PARAMETER NoTrustedPath
    可选。跳过 TRUSTEDPATHS 写入（不推荐）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
[CmdletBinding()]
param(
    [string]$BundleSource = "",
    [switch]$NoTrustedPath
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$text) {
    Write-Host "[安装] $text" -ForegroundColor Cyan
}

function Add-TrustedPathForProfileSet([string]$profilesRoot, [string[]]$pathsToAdd) {
    if (-not (Test-Path $profilesRoot)) { return }

    Get-ChildItem -Path $profilesRoot -ErrorAction SilentlyContinue |
        Where-Object { $_.PSIsContainer } |
        ForEach-Object {
            $variables = Join-Path $_.PSPath "Variables"
            if (-not (Test-Path $variables)) {
                New-Item -Path $variables -Force | Out-Null
            }

            $current = ""
            $prop = Get-ItemProperty -Path $variables -Name "TRUSTEDPATHS" -ErrorAction SilentlyContinue
            if ($null -ne $prop -and $null -ne $prop.TRUSTEDPATHS) { $current = [string]$prop.TRUSTEDPATHS }

            $parts = @($current -split ";" | Where-Object { $_ -ne "" })
            $changed = $false
            foreach ($p in $pathsToAdd) {
                if ($parts -notcontains $p) {
                    $parts += $p
                    $changed = $true
                }
            }

            if ($changed) {
                $value = (($parts -join ";") + ";")
                Set-ItemProperty -Path $variables -Name "TRUSTEDPATHS" -Value $value
                Write-Host ("        已加入受信任路径: " + $_.PSChildName)
            }
        }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($BundleSource)) {
    $BundleSource = Join-Path $scriptDir "CadImeSwitch.bundle"
}

if (-not (Test-Path (Join-Path $BundleSource "PackageContents.xml"))) {
    throw "未找到 CadImeSwitch.bundle\PackageContents.xml，请确认安装包完整。"
}

if (Get-Process acad -ErrorAction SilentlyContinue) {
    Write-Host "检测到 AutoCAD 正在运行。请先关闭所有 AutoCAD 窗口，再运行安装脚本。" -ForegroundColor Red
    exit 1
}

$pluginsRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$destBundle = Join-Path $pluginsRoot "CadImeSwitch.bundle"
$dllDir = Join-Path $destBundle "Contents\Windows"

Write-Step "目标目录: $destBundle"

New-Item -ItemType Directory -Force -Path $pluginsRoot | Out-Null
if (Test-Path $destBundle) {
    Remove-Item -Recurse -Force $destBundle
}
Copy-Item -Recurse -Force $BundleSource $destBundle
Write-Step "插件文件已复制"

if (-not $NoTrustedPath) {
    $paths = @($destBundle, $dllDir)
    Add-TrustedPathForProfileSet "HKCU:\Software\Autodesk\AutoCAD\R25.1\ACAD-9101:804\Profiles" $paths
    Add-TrustedPathForProfileSet "HKCU:\Software\Autodesk\AutoCAD\R25.0\ACAD-8101:804\Profiles" $paths
    Add-TrustedPathForProfileSet "HKCU:\Software\Autodesk\AutoCAD\R24.3\ACAD-8101:804\Profiles" $paths
    Write-Step "TRUSTEDPATHS 已更新"
}

$configDir = Join-Path $env:APPDATA "CadImeSwitch"
New-Item -ItemType Directory -Force -Path $configDir | Out-Null

Write-Host ""
Write-Host "安装完成。" -ForegroundColor Green
Write-Host "1. 重启 AutoCAD 2026（天正 T30 会随 CAD 一起启动）。"
Write-Host "2. 在命令行输入 IMESWITCH 可打开设置。"
Write-Host "3. 输入 IMESWITCHSTATUS 可查看当前状态，IMESWITCHSELFTEST 可生成自检报告。"
Write-Host "4. 配置目录: $configDir"
Write-Host ""
