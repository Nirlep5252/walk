param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",

    [string]$Configuration = "Release",

    [switch]$UploadToGitHub,

    [switch]$PublishRelease
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$releaseDir = Join-Path $repoRoot "artifacts\Releases"
$projectPath = Join-Path $repoRoot "src\Walk\Walk.csproj"
$iconPath = Join-Path $repoRoot "src\Walk\Assets\walk-app.ico"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $FilePath $($Arguments -join ' ')"
    }
}

function Try-InvokeStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    return $LASTEXITCODE -eq 0
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Invoke-Step "dotnet" @("tool", "restore")
Invoke-Step "powershell" @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $repoRoot "scripts\Generate-BrandIcons.ps1"))
Invoke-Step "dotnet" @("test", "$repoRoot\Walk.sln", "-c", $Configuration)
Invoke-Step "dotnet" @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:PublishReadyToRun=true",
    "-p:Version=$Version",
    "-o", $publishDir
)

$downloadArgs = @(
    "vpk",
    "download",
    "github",
    "--repoUrl", "https://github.com/Nirlep5252/walk",
    "--channel", "win",
    "--outputDir", $releaseDir
)

if ($env:GITHUB_TOKEN) {
    $downloadArgs += @("--token", $env:GITHUB_TOKEN)
}

Try-InvokeStep "dotnet" $downloadArgs | Out-Null

Invoke-Step "dotnet" @(
    "vpk",
    "pack",
    "--packId", "Walk",
    "--packVersion", $Version,
    "--packTitle", "Walk",
    "--packAuthors", "Nirlep5252",
    "--channel", "win",
    "--runtime", $Runtime,
    "--mainExe", "Walk.exe",
    "--packDir", $publishDir,
    "--outputDir", $releaseDir,
    "--icon", $iconPath
)

if ($UploadToGitHub) {
    if (-not $env:GITHUB_TOKEN) {
        throw "GITHUB_TOKEN must be set to upload release packages."
    }

    $uploadArgs = @(
        "vpk",
        "upload",
        "github",
        "--repoUrl", "https://github.com/Nirlep5252/walk",
        "--token", $env:GITHUB_TOKEN,
        "--channel", "win",
        "--outputDir", $releaseDir,
        "--releaseName", "Walk v$Version",
        "--tag", "v$Version"
    )

    if ($PublishRelease) {
        $uploadArgs += "--publish"
    }

    Invoke-Step "dotnet" $uploadArgs
}
