# ToireMidi2Key 安装包打包脚本（Inno Setup 版，推荐）
#
# 用法（任意目录）：powershell -ExecutionPolicy Bypass -File installer\build-inno.ps1
#
# 做三件事：
#   1. 发布【自包含】单文件版到 build\app（目标机器不需要预装 .NET 运行时）
#   2. 清掉调试符号（原生库 pdb 有 200+MB）与首次运行生成的 config.json
#   3. 用 Inno Setup 编译 installer\ToireMidi2Key.iss → dist\ToireMidi2Key-<版本>-Setup.exe
#
# 依赖：Inno Setup 6（默认找 %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe，
#       也可用环境变量 INNO_ISCC 指定，或在 PATH 里放 iscc）
# 注意：脚本文件本身含中文，必须以 UTF-8【带 BOM】保存，PowerShell 5.1 才能正确解析。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = '0.1.0'
$setup = Join-Path $root "dist\ToireMidi2Key-$version-Setup.exe"

# 找编译器
$iscc = $env:INNO_ISCC
if (-not $iscc) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { $iscc = $c; break } }
}
if (-not $iscc) {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) { throw '未找到 Inno Setup 编译器 ISCC.exe（可设环境变量 INNO_ISCC 指定）' }

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

Write-Host '[3/3] 用 Inno Setup 编译安装包…' -ForegroundColor Cyan
Remove-Item $setup -Force -ErrorAction SilentlyContinue
& $iscc (Join-Path $root 'installer\ToireMidi2Key.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC 编译失败' }

Write-Host ('完成: ' + $setup) -ForegroundColor Green
Write-Host ('体积: ' + [Math]::Round((Get-Item $setup).Length / 1MB, 1) + ' MB')
