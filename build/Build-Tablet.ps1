<#
.SYNOPSIS
    Baut die Tablet-App (Android, Pose Studio Swarm) als installierbare APK nach publish\.
    Voraussetzung: .NET-Workload maui-android, Android SDK (C:\Android\Sdk), JDK 17.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\PoseStudio.Tablet\PoseStudio.Tablet.csproj'
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version   # gleiche Version wie das Windows-Programm
$out = Join-Path $root "publish\tablet-$version"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish $proj -c Release -f net10.0-android -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }

$apk = Get-ChildItem $out -Filter '*-Signed.apk' | Select-Object -First 1
if (-not $apk) { throw "Keine signierte APK in $out" }
$target = Join-Path $root "publish\PoseStudioSwarm-$version.apk"
Copy-Item $apk.FullName $target -Force
Remove-Item $out -Recurse -Force
Write-Host "Fertig: $target"
