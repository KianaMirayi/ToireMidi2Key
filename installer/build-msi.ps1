# ToireMidi2Key 安装包打包脚本
#
# 用法（在任意目录）：powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1
#
# 做三件事：
#   1. 发布【自包含】单文件版到 build\app（用户机器不需要预装 .NET 运行时）
#   2. 清掉调试符号（原生库 pdb 有 200+MB）与首次运行生成的 config.json
#   3. 用 WiX 5 生成 dist\ToireMidi2Key-<版本>-Setup.msi（每用户安装，不需管理员）
#
# 注意：WiX 的 SourceFile 相对路径按【当前工作目录】解析，所以脚本会先切到仓库根目录。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"

$version = '0.1.0'
$msi = Join-Path $root "dist\ToireMidi2Key-$version-Setup.msi"

Write-Host '[1/3] 发布自包含单文件版…' -ForegroundColor Cyan
Remove-Item build -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src\ToireMidi2Key.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false `
    -o build\app --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

Write-Host '[2/3] 清理调试符号与运行时生成的 config.json…' -ForegroundColor Cyan
Get-ChildItem build\app -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
Remove-Item build\app\config.json -Force -ErrorAction SilentlyContinue

Write-Host '[3/3] 生成 MSI…' -ForegroundColor Cyan
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw '未找到 wix，请先执行：dotnet tool install --global wix --version 5.0.2'
}
Remove-Item $msi -Force -ErrorAction SilentlyContinue
wix build installer\ToireMidi2Key.wxs -arch x64 -o $msi
if ($LASTEXITCODE -ne 0) { throw 'wix build 失败' }

Write-Host ('完成: ' + $msi) -ForegroundColor Green
Write-Host ('体积: ' + [Math]::Round((Get-Item $msi).Length / 1MB, 1) + ' MB')
