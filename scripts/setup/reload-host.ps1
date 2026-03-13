param(
    [string]$SamplePath = "",
    [int]$PostLaunchDelaySeconds = 8,
    [int]$PostOpenDelaySeconds = 10
)

$ErrorActionPreference = "Stop"

$hostLog = Join-Path $env:LOCALAPPDATA "Adze\logs\host.log"
$openScript = "C:\SW_plugin\scripts\setup\open-sample-document.ps1"
$sldworksFs = "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\sldworks_fs.exe"

& powershell.exe -NoProfile -File "C:\SW_plugin\scripts\setup\stop-solidworks-processes.ps1"

if ((Test-Path $sldworksFs) -and -not (Get-Process -Name "sldworks_fs" -ErrorAction SilentlyContinue)) {
    Start-Process $sldworksFs
    Start-Sleep -Seconds 4
}

& powershell.exe -NoProfile -File "C:\SW_plugin\scripts\setup\build-host.ps1"
& powershell.exe -NoProfile -File "C:\SW_plugin\scripts\setup\unregister-host-addin.ps1"
& powershell.exe -NoProfile -File "C:\SW_plugin\scripts\setup\register-host-addin.ps1"

& powershell.exe -NoProfile -File "C:\SW_plugin\scripts\setup\launch-and-check-host.ps1"
Start-Sleep -Seconds $PostLaunchDelaySeconds

if ($SamplePath -and (Test-Path $SamplePath)) {
    try {
        & powershell.exe -NoProfile -File $openScript -Path $SamplePath | Out-Null
    }
    catch {
        Start-Process $SamplePath
    }

    Start-Sleep -Seconds $PostOpenDelaySeconds
}

if (Test-Path $hostLog) {
    Get-Content $hostLog -Tail 200
}
