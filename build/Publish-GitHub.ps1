<#
.SYNOPSIS
    Veröffentlicht die aktuelle Version auf GitHub: baut das Windows-Paket (ohne die optionalen
    RTMW3D-/YOLOX-Modelle) und die Tablet-APK, pusht master und legt das Release v<Version>
    mit beiden Dateien an. Obtainium holt sich die APK aus dem neuesten Release.
.PARAMETER Notes
    Text für das Release (Änderungen). Ohne Angabe: Betreff der Commits seit dem letzten Release.
.NOTES
    Voraussetzungen: gh (angemeldet), sauberer Arbeitsstand, Version in Directory.Build.props hochgezählt und committet.
#>
param([string]$Notes)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = 'C:\Program Files\GitHub CLI\gh.exe' }
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
$tag = "v$version"

if (git -C $root status --porcelain) { throw "Arbeitsstand nicht sauber – erst committen." }
if (git -C $root tag --list $tag) { throw "Tag $tag existiert schon – Version in Directory.Build.props hochzählen." }

& (Join-Path $PSScriptRoot 'Build-Package.ps1') -Public
& (Join-Path $PSScriptRoot 'Build-Tablet.ps1')
$zip = Join-Path $root "publish\PoseStudio-$version-win-x64.zip"
$apk = Join-Path $root "publish\PoseStudioSwarm-$version.apk"
foreach ($f in $zip, $apk) { if (-not (Test-Path $f)) { throw "Fehlt: $f" } }

if (-not $Notes) {
    $last = git -C $root describe --tags --abbrev=0 2>$null
    $range = if ($last) { "$last..HEAD" } else { "HEAD" }
    $Notes = (git -C $root log $range --format='- %s' --no-merges | Select-Object -First 30) -join "`n"
}
$Notes += "`n`n**Dateien:** ``PoseStudio-$version-win-x64.zip`` (Windows, entpacken und starten) · ``PoseStudioSwarm-$version.apk`` (Tablet-App, Android 8+).`nDie optionalen RTMW3D-Modelle für 'Pose aus Foto' sind nicht enthalten – siehe README."

git -C $root tag -a $tag -m "Pose Studio $version"
git -C $root push origin master
git -C $root push origin $tag
& $gh release create $tag $zip $apk --repo temak1238/PoseStudio --title "Pose Studio $version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "gh release create fehlgeschlagen" }
Write-Host "Veröffentlicht: https://github.com/temak1238/PoseStudio/releases/tag/$tag"
