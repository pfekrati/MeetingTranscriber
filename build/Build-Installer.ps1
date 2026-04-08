<#
.SYNOPSIS
    Builds, publishes, and optionally signs the Meeting Transcriber installer.

.DESCRIPTION
    This script:
    1. Publishes the app as a self-contained, single-file executable
    2. Signs the executable with your code signing certificate (if provided)
    3. Creates an MSIX package for easy installation
    4. Signs the MSIX package (if certificate provided)

.PARAMETER CertificatePath
    Path to your .pfx code signing certificate file.
    If omitted, the build produces unsigned artifacts.

.PARAMETER CertificatePassword
    Password for the .pfx certificate. If the certificate has no password, omit this.

.PARAMETER CertificateThumbprint
    Thumbprint of a certificate installed in the Windows certificate store.
    Use this instead of CertificatePath when the cert is in the store.

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

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "MeetingTranscriber\MeetingTranscriber.csproj"
$PublishDir = Join-Path $RepoRoot "artifacts\publish"
$InstallerDir = Join-Path $RepoRoot "artifacts\installer"

$AppName = "MeetingTranscriber"
$AppDisplayName = "Meeting Transcriber"
$Publisher = "Pooyan Fekrati"
$PublisherDN = "CN=Pooyan Fekrati"
$Version = "1.0.0.0"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Meeting Transcriber - Build & Package" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1: Clean previous artifacts
# ---------------------------------------------------------------------------
Write-Host "[1/5] Cleaning previous artifacts..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
if (Test-Path $InstallerDir) { Remove-Item $InstallerDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null

# ---------------------------------------------------------------------------
# Step 2: Publish self-contained single-file executable
# ---------------------------------------------------------------------------
Write-Host "[2/5] Publishing self-contained app..." -ForegroundColor Yellow
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
$SigningAvailable = $false
if ($CertificatePath -or $CertificateThumbprint) {
    Write-Host "[3/5] Signing executable..." -ForegroundColor Yellow

    $signToolPath = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
                    Sort-Object FullName -Descending | Select-Object -First 1

    if (-not $signToolPath) {
        Write-Host "  WARNING: signtool.exe not found. Install Windows SDK to enable signing." -ForegroundColor DarkYellow
        Write-Host "  Skipping signing step." -ForegroundColor DarkYellow
    }
    else {
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

        $signArgs += $ExePath
        & $signToolPath.FullName @signArgs

        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Executable signed successfully." -ForegroundColor Green
            $SigningAvailable = $true
        }
        else {
            Write-Host "  WARNING: Signing failed. Continuing with unsigned build." -ForegroundColor DarkYellow
        }
    }
}
else {
    Write-Host "[3/5] Skipping signing (no certificate provided)." -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Step 4: Create MSIX package
# ---------------------------------------------------------------------------
Write-Host "[4/5] Creating MSIX installer package..." -ForegroundColor Yellow

# Check for makeappx
$makeAppx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1

if (-not $makeAppx) {
    Write-Host "  WARNING: makeappx.exe not found. Install Windows 10 SDK." -ForegroundColor DarkYellow
    Write-Host "  Creating ZIP package instead..." -ForegroundColor DarkYellow

    $zipPath = Join-Path $InstallerDir "$AppName-$Version-win-x64.zip"
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $zipPath -Force
    Write-Host "  Created: $zipPath" -ForegroundColor Green
}
else {
    # Prepare logo assets — MSIX requires specific sizes.
    # Scale the source PNG to the required dimensions.
    $iconSource = Join-Path $RepoRoot "MeetingTranscriber\Resources\transcript.png"

    Add-Type -AssemblyName System.Drawing
    $requiredAssets = @(
        @{ Name = "Square44x44Logo.png";  Size = 44  },
        @{ Name = "Square150x150Logo.png"; Size = 150 },
        @{ Name = "StoreLogo.png";         Size = 50  }
    )
    foreach ($asset in $requiredAssets) {
        $destPath = Join-Path $PublishDir $asset.Name
        if (-not (Test-Path $destPath)) {
            $srcImg = [System.Drawing.Image]::FromFile($iconSource)
            $scaled = New-Object System.Drawing.Bitmap($asset.Size, $asset.Size)
            $g = [System.Drawing.Graphics]::FromImage($scaled)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.DrawImage($srcImg, 0, 0, $asset.Size, $asset.Size)
            $g.Dispose()
            $scaled.Save($destPath, [System.Drawing.Imaging.ImageFormat]::Png)
            $scaled.Dispose()
            $srcImg.Dispose()
        }
    }

    # Generate AppxManifest.xml
    $manifestContent = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="rescap">

  <Identity Name="PooyanFekrati.$AppName"
            Publisher="$PublisherDN"
            Version="$Version"
            ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>$AppDisplayName</DisplayName>
    <PublisherDisplayName>$Publisher</PublisherDisplayName>
    <Description>Records audio, transcribes speech in real time, and generates AI-powered meeting summaries.</Description>
    <Logo>StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application Id="$AppName"
                 Executable="$AppName.exe"
                 EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="$AppDisplayName"
                          Description="Meeting Transcriber"
                          BackgroundColor="transparent"
                          Square150x150Logo="Square150x150Logo.png"
                          Square44x44Logo="Square44x44Logo.png" />
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <DeviceCapability Name="microphone" />
  </Capabilities>
</Package>
"@

    $manifestPath = Join-Path $PublishDir "AppxManifest.xml"
    # Write WITHOUT BOM — makeappx rejects UTF-8 with BOM
    [System.IO.File]::WriteAllText($manifestPath, $manifestContent, (New-Object System.Text.UTF8Encoding $false))

    $msixPath = Join-Path $InstallerDir "$AppName-$Version-win-x64.msix"
    & $makeAppx.FullName pack /d $PublishDir /p $msixPath /o

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Created: $msixPath" -ForegroundColor Green
    }
    else {
        Write-Host "  WARNING: MSIX packaging failed. Creating ZIP instead..." -ForegroundColor DarkYellow
        $zipPath = Join-Path $InstallerDir "$AppName-$Version-win-x64.zip"
        Compress-Archive -Path "$PublishDir\*" -DestinationPath $zipPath -Force
        Write-Host "  Created: $zipPath" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Step 5: Sign the MSIX package (optional)
# ---------------------------------------------------------------------------
$msixPath = Join-Path $InstallerDir "$AppName-$Version-win-x64.msix"
if ((Test-Path $msixPath) -and ($CertificatePath -or $CertificateThumbprint)) {
    Write-Host "[5/5] Signing MSIX package..." -ForegroundColor Yellow

    $signToolPath = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
                    Sort-Object FullName -Descending | Select-Object -First 1

    if ($signToolPath) {
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

        $signArgs += $msixPath
        & $signToolPath.FullName @signArgs

        if ($LASTEXITCODE -eq 0) {
            Write-Host "  MSIX package signed successfully." -ForegroundColor Green
        }
        else {
            Write-Host "  WARNING: MSIX signing failed." -ForegroundColor DarkYellow
        }
    }
}
else {
    Write-Host "[5/5] Skipping MSIX signing." -ForegroundColor DarkGray
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
if (-not ($CertificatePath -or $CertificateThumbprint)) {
    Write-Host "NOTE: Artifacts are UNSIGNED. To sign, re-run with:" -ForegroundColor DarkYellow
    Write-Host "  .\build\Build-Installer.ps1 -CertificatePath 'path\to\cert.pfx' -CertificatePassword 'password'" -ForegroundColor DarkYellow
    Write-Host ""
}
Write-Host "Self-contained executable: $ExePath" -ForegroundColor White
Write-Host ""
