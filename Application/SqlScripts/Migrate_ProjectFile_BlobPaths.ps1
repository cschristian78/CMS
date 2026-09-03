# One-time migration: move tProjectFile blobs to
# projects/{ClientShortName}/{RenewalYear}/{ProjectID}/{ProjectFileID}/{safeFileName}
# and update BlobPath / BlobUrl. Safe to re-run (skips already-migrated paths).

$ErrorActionPreference = 'Stop'

$baseUrl = 'https://stacanalyticstdltest.blob.core.windows.net/cms'
$sas = 'sv=2020-02-10&spr=https&st=2026-08-12T16%3A53%3A43Z&se=2045-08-13T16%3A53%3A00Z&sr=c&sp=racwlm&sig=kZ1IpbUlPoeR%2Fe%2F4InYoGZJO0bayTL30pu1j2uI8i%2Bc%3D'
$sqlServer = 'vmus-sql-099'
$sqlDb = 'BMS_CMS'

function Sanitize-FileName([string]$name) {
    if ($null -eq $name) { $name = '' }
    $n = [System.IO.Path]::GetFileName($name.Trim())
    if ([string]::IsNullOrWhiteSpace($n)) { $n = 'upload.bin' }
    foreach ($c in [System.IO.Path]::GetInvalidFileNameChars()) {
        $n = $n.Replace($c, '_')
    }
    $n = $n.Replace(' ', '_')
    if ($n.Length -gt 180) {
        $ext = [System.IO.Path]::GetExtension($n)
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($n)
        $maxStem = [Math]::Max(1, 180 - $ext.Length)
        if ($stem.Length -gt $maxStem) { $stem = $stem.Substring(0, $maxStem) }
        $n = $stem + $ext
    }
    return $n
}

function Sanitize-PathSegment([string]$value, [string]$fallback) {
    if ($null -eq $value) { $value = '' }
    $n = $value.Trim()
    if ([string]::IsNullOrWhiteSpace($n)) { $n = $fallback }
    foreach ($c in [System.IO.Path]::GetInvalidFileNameChars()) {
        $n = $n.Replace($c, '_')
    }
    $n = $n.Replace(' ', '_').Replace('/', '_').Replace('\', '_')
    while ($n.Contains('__')) { $n = $n.Replace('__', '_') }
    $n = $n.Trim('_')
    if ([string]::IsNullOrWhiteSpace($n)) { $n = $fallback }
    if ($n.Length -gt 80) { $n = $n.Substring(0, 80).Trim('_') }
    return $n
}

function Normalize-BlobPath([string]$pathOrUrl) {
    if ($null -eq $pathOrUrl) { $pathOrUrl = '' }
    $v = $pathOrUrl.Trim()
    if ([string]::IsNullOrWhiteSpace($v)) { return '' }
    if ($v.StartsWith($baseUrl, [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring($baseUrl.Length).TrimStart('/')
    }
    $q = $v.IndexOf('?')
    if ($q -ge 0) { $v = $v.Substring(0, $q) }
    return $v.TrimStart('/')
}

function Copy-Blob([string]$oldPath, [string]$newPath) {
    $src = "$baseUrl/$oldPath`?$sas"
    $dest = "$baseUrl/$newPath`?$sas"
    $headers = @{
        'x-ms-version'     = '2020-04-08'
        'x-ms-copy-source' = $src
    }
    $resp = Invoke-WebRequest -Method Put -Uri $dest -Headers $headers -UseBasicParsing
    $status = [string]$resp.Headers['x-ms-copy-status']
    $waited = 0
    while ($status -eq 'pending' -and $waited -lt 120) {
        Start-Sleep -Seconds 1
        $waited++
        $probe = Invoke-WebRequest -Method Head -Uri $dest -Headers @{ 'x-ms-version' = '2020-04-08' } -UseBasicParsing
        $status = [string]$probe.Headers['x-ms-copy-status']
    }
    if ($status -eq 'pending') {
        throw "Copy timed out for $newPath"
    }
    if ($status -eq 'failed') {
        throw "Copy failed for $newPath"
    }
}

function Delete-Blob([string]$path) {
    $uri = "$baseUrl/$path`?$sas"
    try {
        Invoke-WebRequest -Method Delete -Uri $uri -Headers @{ 'x-ms-version' = '2020-04-08' } -UseBasicParsing | Out-Null
    } catch {
        Write-Warning "Could not delete old blob $path : $($_.Exception.Message)"
    }
}

$query = @"
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SELECT
  f.ProjectFileID,
  f.OriginalFileName,
  f.BlobPath,
  p.ProjectID,
  ISNULL(YEAR(p.RenewalDate), YEAR(SYSUTCDATETIME())) AS RenewalYear,
  ISNULL(NULLIF(LTRIM(RTRIM(c.ClientShortName)), ''), ISNULL(NULLIF(LTRIM(RTRIM(c.ClientName)), ''), CONCAT('Client', c.ClientID))) AS ClientKey
FROM dbo.tProjectFile f
INNER JOIN dbo.tProjectRef pr ON pr.ProjectRefID = f.ProjectRefID
INNER JOIN dbo.tProject p ON p.ProjectID = pr.ProjectID
INNER JOIN dbo.tClientRef cr ON cr.ClientRefID = pr.ClientRefID
INNER JOIN dbo.tClient c ON c.ClientID = cr.ClientID
ORDER BY f.ProjectFileID;
"@

$outPath = Join-Path $env:TEMP 'cms_projectfile_blob_migrate.txt'
if (Test-Path $outPath) { Remove-Item $outPath -Force }
sqlcmd -S $sqlServer -d $sqlDb -E -s "|" -W -h -1 -Q $query -o $outPath | Out-Null

$rows = @()
Get-Content $outPath | ForEach-Object {
    $line = $_.Trim()
    if ([string]::IsNullOrWhiteSpace($line)) { return }
    if ($line -match 'rows affected') { return }
    $parts = $line -split '\|', 6
    if ($parts.Count -lt 6) { return }
    if ($parts[0].Trim() -notmatch '^\d+$') { return }
    $rows += [pscustomobject]@{
        ProjectFileID    = [int]$parts[0].Trim()
        OriginalFileName = $parts[1].Trim()
        BlobPath         = $parts[2].Trim()
        ProjectID        = [int]$parts[3].Trim()
        RenewalYear      = [int]$parts[4].Trim()
        ClientKey        = $parts[5].Trim()
    }
}

Write-Host "Found $($rows.Count) project file(s) to evaluate."

$migrated = 0
$skipped = 0
$failed = 0

foreach ($r in $rows) {
    $oldPath = Normalize-BlobPath $r.BlobPath
    $clientSeg = Sanitize-PathSegment $r.ClientKey 'UnknownClient'
    $safeName = Sanitize-FileName $r.OriginalFileName
    $newPath = "projects/$clientSeg/$($r.RenewalYear)/$($r.ProjectID)/$($r.ProjectFileID)/$safeName"
    $newUrl = "$baseUrl/$newPath"

    if ($oldPath -eq $newPath) {
        Write-Host "SKIP already migrated: ProjectFileID $($r.ProjectFileID)"
        $skipped++
        continue
    }

    Write-Host "MIGRATE $($r.ProjectFileID): $oldPath -> $newPath"
    try {
        Copy-Blob -oldPath $oldPath -newPath $newPath

        $safeSqlPath = $newPath.Replace("'", "''")
        $safeSqlUrl = $newUrl.Replace("'", "''")
        $updateSqlPath = Join-Path $env:TEMP ("cms_blob_update_{0}.sql" -f $r.ProjectFileID)
        @"
UPDATE dbo.tProjectFile
   SET BlobPath = N'$safeSqlPath',
       BlobUrl = N'$safeSqlUrl',
       ModifiedDate = SYSUTCDATETIME()
 WHERE ProjectFileID = $($r.ProjectFileID);
"@ | Set-Content -Path $updateSqlPath -Encoding UTF8
        $updateOut = sqlcmd -S $sqlServer -d $sqlDb -E -i $updateSqlPath 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "SQL update failed: $updateOut"
        }
        Remove-Item $updateSqlPath -Force -ErrorAction SilentlyContinue

        if ($oldPath -ne $newPath -and -not [string]::IsNullOrWhiteSpace($oldPath)) {
            Delete-Blob -path $oldPath
        }

        Write-Host "  OK ProjectFileID $($r.ProjectFileID)"
        $migrated++
    } catch {
        Write-Host "  FAIL ProjectFileID $($r.ProjectFileID): $($_.Exception.Message)" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "Done. Migrated=$migrated Skipped=$skipped Failed=$failed"
