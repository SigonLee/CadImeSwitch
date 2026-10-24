<#
.SYNOPSIS
    编译插件并生成可分发安装包。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -DotNet "E:\...\.dotnet\dotnet.exe"
#>
[CmdletBinding()]
param(
    [string]$DotNet = "dotnet",
    [string]$Configuration = "Release",
    [string]$Version = "1.3.8"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\CadImeSwitch\CadImeSwitch.csproj"
$bundle = Join-Path $root "installer\CadImeSwitch.bundle"
$dllSource = Join-Path $root "src\CadImeSwitch\bin\$Configuration\CadImeSwitch.dll"
$dllTargetDir = Join-Path $bundle "Contents\Windows"
$dist = Join-Path $root "dist"

Write-Host "[构建] 编译 $project" -ForegroundColor Cyan
& $DotNet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

if (-not (Test-Path $dllSource)) { throw "未找到编译输出: $dllSource" }

New-Item -ItemType Directory -Force -Path $dllTargetDir | Out-Null
Copy-Item -Force $dllSource (Join-Path $dllTargetDir "CadImeSwitch.dll")
$iconSource = Join-Path (Split-Path -Parent $root) "icon.png"
if (Test-Path $iconSource) {
    Copy-Item -Force $iconSource (Join-Path $dllTargetDir "icon.png")
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist ("CadImeSwitch-" + $Version + ".zip")
if (Test-Path $zip) { Remove-Item -Force $zip }

# 打包为：CadImeSwitch.bundle/ + install.ps1 + uninstall.ps1
$stage = Join-Path $dist "_stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -Recurse -Force $bundle (Join-Path $stage "CadImeSwitch.bundle")
Copy-Item -Force (Join-Path $root "installer\install.ps1") (Join-Path $stage "install.ps1")
Copy-Item -Force (Join-Path $root "installer\uninstall.ps1") (Join-Path $stage "uninstall.ps1")
Copy-Item -Force (Join-Path $root "installer\install.cmd") (Join-Path $stage "install.cmd")
Copy-Item -Force (Join-Path $root "installer\uninstall.cmd") (Join-Path $stage "uninstall.cmd")

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Remove-Item -Recurse -Force $stage

Write-Host ""
Write-Host "[构建] 完成" -ForegroundColor Green
Write-Host "  DLL: $dllSource"
Write-Host "  Bundle: $bundle"
Write-Host "  安装包: $zip"
