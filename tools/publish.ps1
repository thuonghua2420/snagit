<#
.SYNOPSIS
  Builds a release of SnapGit into artifacts/publish and, when Inno Setup is installed, the installer into artifacts/installer.

.PARAMETER SelfContained
  Bundle the .NET runtime (about 150 MB, runs on machines without .NET 8). Default: framework-dependent
  (small; the installer then requires the ".NET 8 Desktop Runtime").

.EXAMPLE
  pwsh tools/publish.ps1                      # small build
  pwsh tools/publish.ps1 -SelfContained       # fully self-contained build
#>
param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root 'artifacts\publish'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

Write-Host "Publishing SnapGit $version (self-contained: $($SelfContained.IsPresent)) ..."
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root 'src\SnapGit.App\SnapGit.App.csproj') -c Release -r win-x64 `
    --self-contained:$($SelfContained.IsPresent) `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$exe = Join-Path $out 'SnapGit.exe'
Write-Host ("Built {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))

# Installer (optional): needs Inno Setup 6  https://jrsoftware.org/isinfo.php
$iscc = @("$env:ProgramFiles\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc "/DAppVersion=$version" "/DSourceDir=$out" "/DSelfContained=$([int]$SelfContained.IsPresent)" (Join-Path $root 'installer\SnapGit.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
    Write-Host "Installer written to artifacts\installer"
} else {
    Write-Host 'Inno Setup not found: skipping the installer (the published folder is ready to zip).'
}
