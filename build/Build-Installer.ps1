<#
.SYNOPSIS
    Builds, publishes, and optionally signs the Meeting Transcriber installer.

.DESCRIPTION
    This script:
    1. Publishes the app as a self-contained, single-file executable
    2. Signs the executable with your code-signing certificate (if provided)
    3. Creates an MSI installer using WiX v5
    4. Signs the MSI (if certificate provided)

.PARAMETER CertificatePath
    Path to a .pfx code-signing certificate. If omitted, artifacts are unsigned.

.PARAMETER CertificatePassword
    Password for the .pfx certificate. Omit if the certificate has no password.

.PARAMETER CertificateThumbprint
    SHA-1 thumbprint of a certificate installed in the Windows certificate store.
    Use this instead of CertificatePath when the cert is already in the store.

.PARAMETER Configuration
    Build configuration. Default: Release

.EXAMPLE
    # Unsigned build (for local testing)
    .\build\Build-Installer.ps1

    # Signed build with PFX file
    .\build\Build-Installer.ps1 -CertificatePath ".\cert.pfx" -CertificatePassword "mypassword"

    # Signed build with certificate from store
    .\build\Build-Installer.ps1 -CertificateThumbprint "A1B2C3D4..."
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$RuntimeIdentifier = "win-x64",

    [switch]$Sign,

    [string]$CertificatePath,

    [string]$CertificatePassword,

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

function Resolve-ToolPath {
    param(
        [Parameter(Mandatory)]
        [string]$ToolName,
        [string[]]$FallbackPaths = @()
    )

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($path in $FallbackPaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw "Could not find '$ToolName'. Install it and ensure it is available on PATH."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "MeetingTranscriber\MeetingTranscriber.csproj"
$issPath = Join-Path $PSScriptRoot "MeetingTranscriber.iss"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishDir = Join-Path $artifactsRoot "publish"
$installerDir = Join-Path $artifactsRoot "installer"
$certsDir = Join-Path $repoRoot "certs"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

if (-not (Test-Path $issPath)) {
    throw "Installer script not found: $issPath"
}

[xml]$projectXml = Get-Content -Path $projectPath
$appVersion = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    $appVersion = "1.0.0"
}

Write-Host "Publishing MeetingTranscriber ($appVersion)..." -ForegroundColor Cyan

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o $publishDir

$appExePath = Join-Path $publishDir "MeetingTranscriber.exe"
if (-not (Test-Path $appExePath)) {
    throw "Published executable not found: $appExePath"
}

$isccPath = Resolve-ToolPath -ToolName "iscc.exe" -FallbackPaths @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$innoArgs = @(
    "/Qp",
    "/DMyAppVersion=$appVersion",
    "/DSourceDir=$publishDir",
    "/DOutputDir=$installerDir"
)

if ($Sign) {
    if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
        $defaultPfx = Get-ChildItem -Path $certsDir -Filter "*.pfx" -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $defaultPfx) {
            $CertificatePath = $defaultPfx.FullName
        }
    }

    if ([string]::IsNullOrWhiteSpace($CertificatePath) -or -not (Test-Path $CertificatePath)) {
        throw "Signing requested, but no certificate was found. Provide -CertificatePath or place a .pfx file in certs/."
    }

    if ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $passwordFile = Join-Path $certsDir "password.txt"
        if (Test-Path $passwordFile) {
            $CertificatePassword = (Get-Content -Path $passwordFile -Raw).Trim()
        }
    }

    if ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
        throw "Signing requested, but no certificate password was provided. Use -CertificatePassword or certs/password.txt."
    }

    $signtoolPath = Resolve-ToolPath -ToolName "signtool.exe"
    $certPathResolved = (Resolve-Path $CertificatePath).Path

    Write-Host "Signing published executable..." -ForegroundColor Cyan
    & $signtoolPath sign /f $certPathResolved /p $CertificatePassword /fd sha256 /td sha256 /tr $TimestampUrl /d "Meeting Transcriber" $appExePath
    if ($LASTEXITCODE -ne 0) {
        throw "Signing published executable failed with exit code $LASTEXITCODE"
    }

    Write-Host "Code signing enabled using certificate: $certPathResolved" -ForegroundColor Yellow
}
else {
    Write-Host "Code signing disabled." -ForegroundColor DarkYellow
}

$innoArgs += $issPath

Write-Host "Building Windows installer..." -ForegroundColor Cyan
& $isccPath @innoArgs

if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE"
}

$installerOutput = Get-ChildItem -Path $installerDir -Filter "*setup.exe" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $installerOutput) {
    throw "Installer output not found in: $installerDir"
}

if ($Sign) {
    Write-Host "Signing installer..." -ForegroundColor Cyan
    & $signtoolPath sign /f $certPathResolved /p $CertificatePassword /fd sha256 /td sha256 /tr $TimestampUrl /d "Meeting Transcriber Installer" $installerOutput.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Signing installer failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Installer created: $($installerOutput.FullName)" -ForegroundColor Green
