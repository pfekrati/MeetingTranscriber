<#
.SYNOPSIS
    Builds, publishes, and optionally signs the Meeting Transcriber installer.

.DESCRIPTION
    This script:
    1. Publishes the app as a self-contained, single-file executable
    2. Signs the executable with your code-signing certificate (if provided)
    3. Creates a Windows installer using Inno Setup
    4. Signs the installer (if certificate provided)

    If Inno Setup is not installed the script falls back to a ZIP archive.

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
param(
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$CertificateThumbprint,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Paths & constants
# ---------------------------------------------------------------------------
$RepoRoot     = Split-Path -Parent $PSScriptRoot
$ProjectPath  = Join-Path $RepoRoot "MeetingTranscriber\MeetingTranscriber.csproj"
$PublishDir   = Join-Path $RepoRoot "artifacts\publish"
$InstallerDir = Join-Path $RepoRoot "artifacts\installer"
$IssPath      = Join-Path $PSScriptRoot "MeetingTranscriber.iss"

$AppName    = "MeetingTranscriber"
$AppVersion = "1.0.0"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Meeting Transcriber - Build Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# Helper: locate signtool.exe
# ---------------------------------------------------------------------------
function Find-SignTool {
    $found = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
             Sort-Object FullName -Descending | Select-Object -First 1
    return $found
}

# ---------------------------------------------------------------------------
# Helper: sign a single file
# ---------------------------------------------------------------------------
function Sign-File {
    param([string]$FilePath)

    $signTool = Find-SignTool
    if (-not $signTool) {
        Write-Host "  WARNING: signtool.exe not found. Install Windows SDK to enable signing." -ForegroundColor DarkYellow
        return $false
    }

    $signArgs = @("sign", "/fd", "SHA256", "/tr", "http://timestamp.digicert.com", "/td", "SHA256")

    if ($CertificateThumbprint) {
        $signArgs += "/sha1", $CertificateThumbprint
    }
    elseif ($CertificatePath) {
        $signArgs += "/f", $CertificatePath
        if ($CertificatePassword) {
            $signArgs += "/p", $CertificatePassword
        }
    }

    $signArgs += $FilePath
    & $signTool.FullName @signArgs

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Signed: $FilePath" -ForegroundColor Green
        return $true
    }
    else {
        Write-Host "  WARNING: Signing failed for $FilePath" -ForegroundColor DarkYellow
        return $false
    }
}

# ---------------------------------------------------------------------------
# Step 1: Clean previous artifacts
# ---------------------------------------------------------------------------
Write-Host "[1/4] Cleaning previous artifacts..." -ForegroundColor Yellow
if (Test-Path $PublishDir)   { Remove-Item $PublishDir   -Recurse -Force }
if (Test-Path $InstallerDir) { Remove-Item $InstallerDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir   -Force | Out-Null
New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null

# ---------------------------------------------------------------------------
# Step 2: Publish self-contained single-file executable
# ---------------------------------------------------------------------------
Write-Host "[2/4] Publishing self-contained app..." -ForegroundColor Yellow
dotnet publish $ProjectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $PublishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed." -ForegroundColor Red
    exit 1
}

$ExePath = Join-Path $PublishDir "$AppName.exe"
if (-not (Test-Path $ExePath)) {
    Write-Host "ERROR: Expected output not found: $ExePath" -ForegroundColor Red
    exit 1
}

$ExeSize = [math]::Round((Get-Item $ExePath).Length / 1MB, 1)
Write-Host "  Published: $ExePath ($ExeSize MB)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 3: Sign the executable (optional)
# ---------------------------------------------------------------------------
$HasCert = $CertificatePath -or $CertificateThumbprint
if ($HasCert) {
    Write-Host "[3/4] Signing executable..." -ForegroundColor Yellow
    Sign-File -FilePath $ExePath | Out-Null
}
else {
    Write-Host "[3/4] Skipping signing (no certificate provided)." -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Step 4: Build the installer
# ---------------------------------------------------------------------------
Write-Host "[4/4] Building installer..." -ForegroundColor Yellow

# Locate Inno Setup compiler
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

$installerBuilt = $false

if ($iscc -and (Test-Path $IssPath)) {
    # Build the sign tool parameter for Inno Setup if a certificate is available
    $issSignToolArg = $null
    if ($HasCert) {
        $signToolExe = Find-SignTool
        if ($signToolExe) {
            # Inno Setup SignTool syntax: name=command $f is replaced with filename
            $signCmd = "`"$($signToolExe.FullName)`" sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256"
            if ($CertificateThumbprint) {
                $signCmd += " /sha1 $CertificateThumbprint"
            }
            elseif ($CertificatePath) {
                $signCmd += " /f `"$CertificatePath`""
                if ($CertificatePassword) {
                    $signCmd += " /p `"$CertificatePassword`""
                }
            }
            $signCmd += ' $f'
            $issSignToolArg = "/Ssigntool=$signCmd"
        }
    }

    # Build the full command line for cmd /c — this avoids PowerShell
    # re-interpreting or mangling quotes when passing to ISCC.exe.
    $cmdLine = "`"$iscc`""
    $cmdLine += " /DAppVersion=$AppVersion"
    $cmdLine += " `"/DPublishDir=$PublishDir`""
    $cmdLine += " `"/DInstallerDir=$InstallerDir`""
    $cmdLine += " /DAppExeName=$AppName.exe"

    if ($issSignToolArg) {
        $cmdLine += " /DSignTool=signtool"
        $cmdLine += " `"$issSignToolArg`""
    }
    else {
        $cmdLine += " /DSignTool="
    }

    $cmdLine += " `"$IssPath`""

    Write-Host "  Running Inno Setup compiler..." -ForegroundColor Gray
    cmd /c $cmdLine

    if ($LASTEXITCODE -eq 0) {
        $setupExe = Join-Path $InstallerDir "$AppName-$AppVersion-Setup.exe"
        if (Test-Path $setupExe) {
            $setupSize = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
            Write-Host "  Created installer: $setupExe ($setupSize MB)" -ForegroundColor Green
            $installerBuilt = $true
        }
    }
    else {
        Write-Host "  WARNING: Inno Setup compilation failed. Creating ZIP instead..." -ForegroundColor DarkYellow
    }
}
else {
    Write-Host "  Inno Setup 6 not found. Download free from https://jrsoftware.org/isinfo.php" -ForegroundColor DarkYellow
}

if (-not $installerBuilt) {
    Write-Host "  Creating ZIP archive instead..." -ForegroundColor DarkYellow
    $zipPath = Join-Path $InstallerDir "$AppName-$AppVersion-win-x64.zip"
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $zipPath -Force
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "  Created: $zipPath ($zipSize MB)" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Artifacts:" -ForegroundColor White
Get-ChildItem $InstallerDir | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name) ($size MB)" -ForegroundColor Green
}
Write-Host ""
if (-not $HasCert) {
    Write-Host "NOTE: Artifacts are UNSIGNED. To sign, re-run with:" -ForegroundColor DarkYellow
    Write-Host "  .\build\Build-Installer.ps1 -CertificatePath 'path\to\cert.pfx' -CertificatePassword 'password'" -ForegroundColor DarkYellow
    Write-Host "  or" -ForegroundColor DarkYellow
    Write-Host "  .\build\Build-Installer.ps1 -CertificateThumbprint 'THUMBPRINT'" -ForegroundColor DarkYellow
    Write-Host ""
}
Write-Host "Self-contained executable: $ExePath" -ForegroundColor White
Write-Host ""
