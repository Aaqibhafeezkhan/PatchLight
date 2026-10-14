# Writes the winget manifests for a published release into winget\manifests\g\G4SBO\PatchLight\<version>.
# Submit them to https://github.com/microsoft/winget-pkgs (for example with: wingetcreate submit <folder>).
#   .\scripts\winget-manifest.ps1 -Version 0.4.0
param([Parameter(Mandatory)] [string] $Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$url = "https://github.com/yo-gabriel-portfolio-here/PatchLight/releases/download/v$Version/PatchLight-Setup.exe"
$temp = Join-Path ([IO.Path]::GetTempPath()) "PatchLight-Setup-$Version.exe"
Invoke-WebRequest $url -OutFile $temp -UseBasicParsing
$sha = (Get-FileHash $temp -Algorithm SHA256).Hash
Remove-Item $temp

$dir = Join-Path $root "winget\manifests\g\G4SBO\PatchLight\$Version"
New-Item -ItemType Directory -Force $dir | Out-Null
$id = 'G4SBO.PatchLight'
$schema = '1.6.0'

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$id.yaml") -Encoding utf8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
MinimumOSVersion: 10.0.22000.0
InstallerType: inno
Scope: machine
InstallModes:
- interactive
- silent
UpgradeBehavior: install
ElevationRequirement: elevatesSelf
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$id.installer.yaml") -Encoding utf8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: G4SBO
PublisherUrl: https://g4sbo.com
PublisherSupportUrl: https://github.com/yo-gabriel-portfolio-here/PatchLight/issues
PackageName: PatchLight
PackageUrl: https://github.com/yo-gabriel-portfolio-here/PatchLight
License: MIT
LicenseUrl: https://github.com/yo-gabriel-portfolio-here/PatchLight/blob/main/LICENSE
ShortDescription: Know if a Windows update is safe before you install it.
Description: PatchLight checks every Windows update waiting on your PC against Microsoft's known issues and tech-news reports, shows a green, yellow or red light, pauses only the bad ones and resumes them once Microsoft fixes them.
Tags:
- windows-update
- windows-11
- known-issues
- updates
ReleaseNotesUrl: https://github.com/yo-gabriel-portfolio-here/PatchLight/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$id.locale.en-US.yaml") -Encoding utf8

Write-Host "Manifests written to $dir"
Write-Host "Check them with: winget validate --manifest `"$dir`""
