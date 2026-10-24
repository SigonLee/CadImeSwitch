<#
.SYNOPSIS
    卸载 CAD 输入法自动切换插件（当前用户）。

.PARAMETER RemoveConfig
    同时删除 %APPDATA%\CadImeSwitch 下的配置与日志。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
#>
[CmdletBinding()]
param(
    [switch]$RemoveConfig
)

$ErrorActionPreference = "Stop"

if (Get-Process acad -ErrorAction SilentlyContinue) {
    Write-Host "检测到 AutoCAD 正在运行。请先关闭所有 AutoCAD 窗口，再运行卸载脚本。" -ForegroundColor Red
    exit 1
}

$bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\CadImeSwitch.bundle"

Write-Host "[卸载] 删除插件目录: $bundle" -ForegroundColor Cyan

if (Test-Path $bundle) {
    Remove-Item -Recurse -Force $bundle
    Write-Host "        已删除。" -ForegroundColor Green
} else {
    Write-Host "        目录不存在，跳过。"
}

$profileRoots = @(
    "HKCU:\Software\Autodesk\AutoCAD\R25.1\ACAD-9101:804\Profiles",
    "HKCU:\Software\Autodesk\AutoCAD\R25.0\ACAD-8101:804\Profiles",
    "HKCU:\Software\Autodesk\AutoCAD\R24.3\ACAD-8101:804\Profiles"
)

foreach ($root in $profileRoots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem $root -ErrorAction SilentlyContinue |
        Where-Object { $_.PSIsContainer } |
        ForEach-Object {
            $variables = Join-Path $_.PSPath "Variables"
            $prop = Get-ItemProperty -Path $variables -Name "TRUSTEDPATHS" -ErrorAction SilentlyContinue
            if ($null -eq $prop -or $null -eq $prop.TRUSTEDPATHS) { return }

            $parts = @(([string]$prop.TRUSTEDPATHS) -split ";" |
                Where-Object { $_ -ne "" -and $_ -notmatch "CadImeSwitch\.bundle" })
            $value = if ($parts.Count -gt 0) { ($parts -join ";") + ";" } else { "" }
            Set-ItemProperty -Path $variables -Name "TRUSTEDPATHS" -Value $value
            Write-Host ("        已清理受信任路径: " + $_.PSChildName)
        }
}

if ($RemoveConfig) {
    $configDir = Join-Path $env:APPDATA "CadImeSwitch"
    if (Test-Path $configDir) {
        Remove-Item -Recurse -Force $configDir
        Write-Host "        已删除配置与日志: $configDir" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "卸载完成。重启 AutoCAD 后生效。" -ForegroundColor Green
