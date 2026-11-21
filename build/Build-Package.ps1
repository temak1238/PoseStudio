<#
.SYNOPSIS
    Baut Pose Studio als einzelne, eigenständige EXE (kein .NET auf dem Zielrechner nötig)
    und packt sie als ZIP nach publish\.
.PARAMETER Public
    Ohne die optionalen RTMW3D-/YOLOX-Modelle (Lizenz: nur nicht-kommerziell, ~470 MB) – für das GitHub-Release.
#>
param([switch]$Public)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\PoseStudio\PoseStudio.csproj'
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
$commit = (git -C $root rev-parse --short HEAD 2>$null)
$info = if ($commit) { "$version+$commit" } else { $version }

$out = Join-Path $root "publish\PoseStudio-$version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:InformationalVersion=$info `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }

Get-ChildItem $out -Filter *.pdb | Remove-Item -Force
Get-ChildItem $out -Filter *.lib | Remove-Item -Force   # Import-Bibliotheken aus ONNX Runtime, zur Laufzeit nicht nötig
if ($Public) { Get-ChildItem (Join-Path $out 'Models') -Include 'rtmw3d*.onnx', 'yolox*.onnx' -Recurse | Remove-Item -Force }
Copy-Item (Join-Path $root 'README.md') $out
$zip = Join-Path $root "publish\PoseStudio-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Fertig: $zip"
