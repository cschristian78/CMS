#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Raises IIS requestFiltering maxAllowedContentLength for CMS-Dev1 (fixes HTTP 413.1 on file upload).
#>
param(
    [string] $SiteName = 'CMS-Dev1',
    [long] $MaxAllowedContentLength = 2254857830  # ~2.1 GB (2 GB file + overhead)
)

$ErrorActionPreference = 'Stop'
$appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
if (-not (Test-Path -LiteralPath $appcmd)) {
    throw "appcmd not found at $appcmd"
}

Write-Host "Setting maxAllowedContentLength=$MaxAllowedContentLength on site '$SiteName'..."
& $appcmd set config $SiteName -section:system.webServer/security/requestFiltering "/requestLimits.maxAllowedContentLength:$MaxAllowedContentLength" /commit:apphost
if ($LASTEXITCODE -ne 0) {
    # Site name may differ - try matching by physical path
    Write-Host "Direct site name failed; listing sites:"
    & $appcmd list site
    throw "Failed to set config for '$SiteName'. Re-run with -SiteName matching the site that binds :82."
}

Write-Host "Effective requestFiltering:"
& $appcmd list config $SiteName -section:system.webServer/security/requestFiltering

# Recycle app pools for this site
$apps = & $appcmd list app /site.name:$SiteName
foreach ($line in $apps) {
    if ($line -match 'APP\s+"([^"]+)"') {
        $appPath = $Matches[1]
        $poolLine = & $appcmd list app $appPath /text:apppool.name
        $pool = ($poolLine | Select-Object -First 1).ToString().Trim()
        if ($pool) {
            Write-Host "Recycling app pool '$pool'..."
            & $appcmd recycle apppool /apppool.name:$pool
        }
    }
}

Write-Host "Done. Retry the upload."
