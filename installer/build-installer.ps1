param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $Iscc = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $root 'artifacts\installer-publish'
$output = Join-Path $root 'artifacts\installer'
$projectPath = Join-Path $root 'VideoDownloader.csproj'
[xml] $project = Get-Content -LiteralPath $projectPath -Raw
$version = [string] $project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '專案 Version 必須使用 major.minor.patch 格式。' }

if (-not $Iscc) {
    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if ($command) { $Iscc = $command.Source }
}
if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) {
    if (-not $Iscc) {
        foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
            foreach ($major in @(7, 6)) {
                if ($base) {
                    $candidate = Join-Path $base "Inno Setup $major\ISCC.exe"
                    if (Test-Path -LiteralPath $candidate) { $Iscc = $candidate; break }
                }
            }
            if ($Iscc) { break }
        }
    }
    if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) {
        throw '找不到 Inno Setup Compiler（iscc.exe）。請安裝 Inno Setup 7，或使用 -Iscc 指定完整路徑。'
    }
}

New-Item -ItemType Directory -Force -Path $publish, $output | Out-Null
# Disable Avalonia's optional telemetry build task; it can fail on locked-down machines.
dotnet publish (Join-Path $root 'VideoDownloader.csproj') -c $Configuration -r $Runtime --self-contained true --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -p:UsedAvaloniaProducts= -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失敗，退出碼：$LASTEXITCODE。請先執行 dotnet restore -r $Runtime。" }

$script = Join-Path $root 'installer\VideoDownloader.iss'
& $Iscc "/DPublishDir=$publish" "/DAppVersion=$version" $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 編譯失敗，退出碼：$LASTEXITCODE" }

$installer = Join-Path $output "VideoDownloader-v$version-Setup-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw "找不到輸出安裝包：$installer" }
Write-Output "完成：$installer"
Write-Output (Get-FileHash -LiteralPath $installer -Algorithm SHA256 | Format-List | Out-String)
