param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",

    [string]$Configuration = "Release",

    [string]$ReleaseNotesPath,

    [switch]$UploadToGitHub,

    [switch]$PublishRelease,

    [string]$SignParams,

    [string]$SignTemplate,

    [string]$AzureTrustedSignFile,

    [switch]$RequireSigning
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$releaseDir = Join-Path $repoRoot "artifacts\Releases"
$projectPath = Join-Path $repoRoot "src\Walk\Walk.csproj"
$iconPath = Join-Path $repoRoot "src\Walk\Assets\walk-app.ico"

if ([string]::IsNullOrWhiteSpace($SignParams)) {
    $SignParams = $env:VELOPACK_SIGN_PARAMS
}

if ([string]::IsNullOrWhiteSpace($SignTemplate)) {
    $SignTemplate = $env:VELOPACK_SIGN_TEMPLATE
}

if ([string]::IsNullOrWhiteSpace($AzureTrustedSignFile)) {
    $AzureTrustedSignFile = $env:VELOPACK_AZURE_TRUSTED_SIGN_FILE
}

if (-not $RequireSigning.IsPresent -and $env:VELOPACK_REQUIRE_SIGNING) {
    if ($env:VELOPACK_REQUIRE_SIGNING -match '^(1|true|yes|on)$') {
        $RequireSigning = $true
    }
}

if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $ReleaseNotesPath = Join-Path $repoRoot "docs\releases\$Version.md"
}

if (-not (Test-Path $ReleaseNotesPath -PathType Leaf)) {
    throw "Release notes are mandatory. Create '$ReleaseNotesPath' before publishing version $Version."
}

$releaseNotesContent = Get-Content $ReleaseNotesPath -Raw
if ([string]::IsNullOrWhiteSpace($releaseNotesContent)) {
    throw "Release notes file '$ReleaseNotesPath' is empty. Add markdown changelog content before publishing version $Version."
}

$configuredSigningModes = @(
    -not [string]::IsNullOrWhiteSpace($AzureTrustedSignFile),
    -not [string]::IsNullOrWhiteSpace($SignTemplate),
    -not [string]::IsNullOrWhiteSpace($SignParams)
) | Where-Object { $_ }

if ($configuredSigningModes.Count -gt 1) {
    throw "Configure only one signing mode: AzureTrustedSignFile, SignTemplate, or SignParams."
}

if (-not [string]::IsNullOrWhiteSpace($AzureTrustedSignFile) -and -not (Test-Path $AzureTrustedSignFile -PathType Leaf)) {
    throw "Azure Trusted Signing metadata file '$AzureTrustedSignFile' was not found."
}

if ($RequireSigning -and $configuredSigningModes.Count -eq 0) {
    throw "Signing is required but no signing configuration was provided. Set VELOPACK_AZURE_TRUSTED_SIGN_FILE, VELOPACK_SIGN_TEMPLATE, or VELOPACK_SIGN_PARAMS."
}

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
    "-p:PublishReadyToRun=false",
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

$packArgs = @(
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
    "--icon", $iconPath,
    "--releaseNotes", $ReleaseNotesPath
)

if (-not [string]::IsNullOrWhiteSpace($AzureTrustedSignFile)) {
    $packArgs += @("--azureTrustedSignFile", (Resolve-Path $AzureTrustedSignFile).Path)
} elseif (-not [string]::IsNullOrWhiteSpace($SignTemplate)) {
    $packArgs += @("--signTemplate", $SignTemplate)
} elseif (-not [string]::IsNullOrWhiteSpace($SignParams)) {
    $packArgs += @("--signParams", $SignParams)
}

Invoke-Step "dotnet" $packArgs

if ($configuredSigningModes.Count -eq 0) {
    Get-ChildItem -Path $releaseDir -Filter "*-Setup.exe" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

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
