# Run elevated (Right-click PowerShell -> Run as administrator), then:
#   Set-ExecutionPolicy -Scope Process Bypass; .\repair-cms-iis.ps1
$ErrorActionPreference = "Continue"
$siteName = "CMS-Dev1"
$appPool = "CMS-Dev1"
$phys = "C:\inetpub\wwwroot\CMS-Dev1"
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"

Write-Host "W3SVC=$((Get-Service W3SVC).Status) WAS=$((Get-Service WAS).Status)"
Write-Host "==== Sites before ===="
& $appcmd list site
Write-Host "==== AppPools before ===="
& $appcmd list apppool

$pools = & $appcmd list apppool /name:"$appPool" 2>&1
if ("$pools" -notmatch [regex]::Escape($appPool)) {
  Write-Host "Creating app pool $appPool (No Managed Code)"
  & $appcmd add apppool /name:"$appPool" /managedRuntimeVersion:"" /managedPipelineMode:Integrated
} else {
  & $appcmd set apppool /apppool.name:"$appPool" /managedRuntimeVersion:""
}
& $appcmd start apppool /apppool.name:"$appPool"

$sites = & $appcmd list site /name:"$siteName" 2>&1
if ("$sites" -notmatch [regex]::Escape($siteName)) {
  Write-Host "Creating site $siteName -> $phys binding http://cms-dev"
  & $appcmd add site /name:"$siteName" /physicalPath:"$phys" /bindings:"http/*:80:cms-dev"
} else {
  Write-Host "Updating existing site physical path + binding"
  & $appcmd set vdir "$siteName/" /physicalPath:"$phys"
  $b = & $appcmd list site /name:"$siteName" /text:bindings
  Write-Host "Current bindings: $b"
  if ("$b" -notmatch "cms-dev") {
    & $appcmd set site /site.name:"$siteName" /+bindings.[protocol='http',bindingInformation='*:80:cms-dev']
  }
}

& $appcmd set app "$siteName/" /applicationPool:"$appPool"
& $appcmd start site /site.name:"$siteName"

Write-Host "==== Sites after ===="
& $appcmd list site /name:"$siteName"
Write-Host "==== Probe ===="
try {
  $r = Invoke-WebRequest -Uri "http://cms-dev/" -UseBasicParsing -TimeoutSec 15
  Write-Host "http://cms-dev/ -> $($r.StatusCode) server=$($r.Headers['Server']) len=$($r.Content.Length)"
} catch {
  Write-Host "Probe failed: $($_.Exception.Message)"
  Write-Host "If still 404: in IIS Manager confirm site CMS-Dev1 is Started and physical path is $phys"
}
