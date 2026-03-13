$ErrorActionPreference = "Stop"

$vendorRoot = "C:\Program Files\Dassault Systemes"
$installRoot = "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x"
$interopCandidates = @(
  (Join-Path $installRoot "SOLIDWORKS\SolidWorks.Interop.sldworks.dll"),
  (Join-Path $installRoot "SOLIDWORKS\SolidWorks.Interop.swconst.dll"),
  (Join-Path $installRoot "SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll"),
  (Join-Path $installRoot "SOLIDWORKS\api\redist\SolidWorks.Interop.swconst.dll")
)

$result = [ordered]@{
  vendor_root_exists = Test-Path $vendorRoot
  install_root_exists = Test-Path $installRoot
  interop_hits = @()
  has_msbuild = Test-Path "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
}

foreach ($candidate in $interopCandidates) {
  if (Test-Path $candidate) {
    $result.interop_hits += $candidate
  }
}

if ($result.interop_hits.Count -eq 0 -and $result.vendor_root_exists) {
  $result.interop_hits += Get-ChildItem $vendorRoot -Recurse -Include "SolidWorks.Interop.sldworks.dll", "SolidWorks.Interop.swconst.dll" -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
}

$result.host_ready = $result.install_root_exists -and ($result.interop_hits.Count -ge 2) -and $result.has_msbuild
$result | ConvertTo-Json -Depth 4
