# Builds the downloadable files into dist\:
#   dist\PatchLight\          the Windows app (run PatchLight.exe; keep the whole folder together)
#   dist\patchlight.exe       the command line, one file
#   dist\PatchLight-win-x64.zip   the app folder, zipped (portable)
#   dist\PatchLight-Setup.exe      the installer most people should download (needs Inno Setup 6)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

dotnet publish (Join-Path $root 'src\PatchLight.App') -c Release -o (Join-Path $dist 'PatchLight')
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }

dotnet publish (Join-Path $root 'src\PatchLight.Cli') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $dist 'cli')
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
Move-Item (Join-Path $dist 'cli\patchlight.exe') $dist
Remove-Item (Join-Path $dist 'cli') -Recurse -Force

# Not copied into the app folder: Windows file names ignore case, so patchlight.exe would replace PatchLight.exe.
Compress-Archive -Path (Join-Path $dist 'PatchLight') -DestinationPath (Join-Path $dist 'PatchLight-win-x64.zip')
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    $version = ([xml](Get-Content (Join-Path $root 'src\PatchLight.App\PatchLight.App.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    & $iscc /Q "/DAppVersion=$version" (Join-Path $root 'installer\PatchLight.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
} else {
    Write-Warning 'Inno Setup 6 not found, so no installer was built. Install it with: winget install JRSoftware.InnoSetup'
}
Write-Host "Done: $dist"
