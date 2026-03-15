param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$assemblyPath = Join-Path $repoRoot "src\Adze.Host\bin\$Configuration\Adze.Host.dll"
$classesRoot = "HKCU\Software\Classes"
$solidWorksRoot = "HKCU\Software\SolidWorks"
$implementedCategory = "{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"
$runtimeVersion = "v4.0.30319"

if (-not (Test-Path $assemblyPath)) {
    throw "Host assembly not found: $assemblyPath"
}

$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
$assemblyDescriptor = "{0}, Version={1}, Culture=neutral, PublicKeyToken=null" -f $assemblyName.Name, $assemblyName.Version
$versionKey = $assemblyName.Version.ToString()
$codeBase = ([System.Uri]$assemblyPath).AbsoluteUri

function Set-RegistryValue {
    param(
        [Parameter(Mandatory = $true)][string]$Key,
        [string]$Name = "",
        [Parameter(Mandatory = $true)][string]$Type,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $arguments = @("add", $Key, "/f", "/t", $Type, "/d", $Value)
    if ([string]::IsNullOrWhiteSpace($Name)) {
        $arguments += "/ve"
    }
    else {
        $arguments += @("/v", $Name)
    }

    & reg.exe @arguments | Out-Null
}

function Register-ComClass {
    param(
        [Parameter(Mandatory = $true)][string]$ProgId,
        [Parameter(Mandatory = $true)][string]$Guid,
        [Parameter(Mandatory = $true)][string]$ClassName,
        [Parameter(Mandatory = $true)][string]$DefaultName
    )

    Set-RegistryValue -Key "$classesRoot\$ProgId" -Type REG_SZ -Value $DefaultName
    Set-RegistryValue -Key "$classesRoot\$ProgId\CLSID" -Type REG_SZ -Value $Guid

    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid" -Type REG_SZ -Value $DefaultName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Type REG_SZ -Value "mscoree.dll"
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "ThreadingModel" -Type REG_SZ -Value "Both"
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "Class" -Type REG_SZ -Value $ClassName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "Assembly" -Type REG_SZ -Value $assemblyDescriptor
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "RuntimeVersion" -Type REG_SZ -Value $runtimeVersion
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "CodeBase" -Type REG_SZ -Value $codeBase

    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "Class" -Type REG_SZ -Value $ClassName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "Assembly" -Type REG_SZ -Value $assemblyDescriptor
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "RuntimeVersion" -Type REG_SZ -Value $runtimeVersion
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "CodeBase" -Type REG_SZ -Value $codeBase

    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\ProgId" -Type REG_SZ -Value $ProgId
    & reg.exe add "$classesRoot\CLSID\$Guid\Implemented Categories\$implementedCategory" /f | Out-Null
}

Register-ComClass `
    -ProgId "Adze.Host.AddIn" `
    -Guid "{A2E09EE4-BB43-4A0C-945F-14711F792EFA}" `
    -ClassName "Adze.Host.AddIn.AdzeAddIn" `
    -DefaultName "Adze.Host.AddIn.AdzeAddIn"

Register-ComClass `
    -ProgId "Adze.Host.TaskPaneControl" `
    -Guid "{F4068202-600A-4D6F-973B-DA2048A949CF}" `
    -ClassName "Adze.Host.UI.TaskPaneControl" `
    -DefaultName "Adze.Host.UI.TaskPaneControl"

Set-RegistryValue -Key "$solidWorksRoot\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}" -Type REG_DWORD -Value "1"
Set-RegistryValue -Key "$solidWorksRoot\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}" -Name "Title" -Type REG_SZ -Value "Adze for SOLIDWORKS"
Set-RegistryValue -Key "$solidWorksRoot\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}" -Name "Description" -Type REG_SZ -Value "Native AI assistant add-in for SOLIDWORKS."
Set-RegistryValue -Key "$solidWorksRoot\AddInsStartup\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}" -Type REG_DWORD -Value "1"
