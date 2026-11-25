<#
.SYNOPSIS
  Baut die Danbooru-Tag-Liste für Pose Studio (Autovervollständigung, Tag-Prüfung, Sortierung der LoRA-Chips).
.DESCRIPTION
  Holt über die öffentliche Danbooru-API alle Tags ab einer Mindestzahl an Bildern (ohne Künstler – die kommen aus AnimaDex)
  und die aktiven Aliase dazu und schreibt eine gepackte TSV-Datei:
    name <TAB> kategorie <TAB> anzahl <TAB> alias1,alias2,…
  Kategorien: 0 allgemein, 3 Serie, 4 Charakter, 5 Meta; Anzahl 0 = von Danbooru als veraltet markiert. Unterstriche werden zu Leerzeichen (außer bei Smileys wie ^_^).
.EXAMPLE
  powershell -File tools\booru\Build-TagList.ps1
#>
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\..\src\PoseStudio.SwarmCore\Data\danbooru-tags.tsv.gz'),
    [int]$MinGeneral = 50,
    [int]$MinCharacter = 100,
    [int]$MinMeta = 1000,
    [int]$DelayMs = 200
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$base = 'https://danbooru.donmai.us'
$ua = 'PoseStudio-TagTool/1.0'

function Get-Paged([string]$path, [string]$query) {
    # Cursor-Blättern über die ID (page=b<id>), 1000 je Seite
    $all = New-Object System.Collections.Generic.List[object]
    $cursor = ''
    while ($true) {
        $url = "$base/$path`?limit=1000&$query" + $(if ($cursor) { "&page=b$cursor" } else { '' })
        for ($try = 1; ; $try++) {
            try { $page = Invoke-RestMethod -Uri $url -UserAgent $ua -TimeoutSec 60; break }
            catch { if ($try -ge 5) { throw }; Start-Sleep -Seconds (3 * $try) }
        }
        if (-not $page -or $page.Count -eq 0) { break }
        $all.AddRange([object[]]$page)
        $cursor = ($page | Measure-Object -Property id -Minimum).Minimum
        Write-Host ("  {0}: {1}" -f $path, $all.Count)
        if ($page.Count -lt 1000) { break }
        Start-Sleep -Milliseconds $DelayMs
    }
    return $all
}

function Convert-Name([string]$n) {
    $plain = $n -replace '_', ''
    if ($plain.Length -le 3 -or $n -match '[\^<>@=+|]') { return $n }
    return $n -replace '_', ' '
}

Write-Host 'Tags abrufen …'
$tags = Get-Paged 'tags.json' ("search%5Bpost_count%5D=%3E%3D$MinGeneral&search%5Bis_deprecated%5D=false&only=id,name,category,post_count")
$keep = @{}
foreach ($t in $tags) {
    $min = switch ([int]$t.category) { 0 { $MinGeneral } 3 { $MinCharacter } 4 { $MinCharacter } 5 { $MinMeta } default { -1 } }
    if ($min -lt 0 -or [int]$t.post_count -lt $min) { continue }
    $keep[$t.name] = [pscustomobject]@{ Name = (Convert-Name $t.name); Cat = [int]$t.category; Count = [int]$t.post_count; Aliases = New-Object System.Collections.Generic.List[string] }
}
Write-Host ("Behalten: {0} Tags" -f $keep.Count)

# Veraltete Tags (z. B. „black footwear“, heute „black shoes“/„black boots“): Danbooru hat sie umbenannt, die Modelle kennen sie
# aber aus dem Training und LoRA-Metadaten nutzen sie ständig. Anzahl 0 = veraltet.
Write-Host 'Veraltete Tags abrufen …'
$dep = Get-Paged 'tags.json' 'search%5Bis_deprecated%5D=true&only=id,name,category,post_count'
$nd = 0
foreach ($t in $dep) {
    if ([int]$t.category -notin 0, 3, 4 -or $keep.ContainsKey($t.name)) { continue }
    $keep[$t.name] = [pscustomobject]@{ Name = (Convert-Name $t.name); Cat = [int]$t.category; Count = 0; Aliases = New-Object System.Collections.Generic.List[string] }
    $nd++
}
Write-Host ("Veraltete Tags: {0}" -f $nd)

Write-Host 'Aliase abrufen …'
$aliases = Get-Paged 'tag_aliases.json' 'search%5Bstatus%5D=active&only=id,antecedent_name,consequent_name'
$na = 0
foreach ($a in $aliases) {
    $t = $keep[$a.consequent_name]
    if ($t) { $t.Aliases.Add((Convert-Name $a.antecedent_name)); $na++ }
}
Write-Host ("Aliase zu behaltenen Tags: {0}" -f $na)

$sb = New-Object System.Text.StringBuilder
foreach ($t in ($keep.Values | Sort-Object Count -Descending)) {
    $al = ($t.Aliases | Where-Object { $_ -notmatch "[`t`r`n,]" }) -join ','
    [void]$sb.Append($t.Name).Append("`t").Append($t.Cat).Append("`t").Append($t.Count).Append("`t").Append($al).Append("`n")
}
$bytes = [Text.Encoding]::UTF8.GetBytes($sb.ToString())
$dir = Split-Path $Out -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$fs = [IO.File]::Create($Out)
$gz = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
$gz.Write($bytes, 0, $bytes.Length); $gz.Dispose(); $fs.Dispose()
Write-Host ("Geschrieben: {0} ({1:N0} KB, ungepackt {2:N0} KB)" -f (Resolve-Path $Out), ((Get-Item $Out).Length / 1KB), ($bytes.Length / 1KB))
