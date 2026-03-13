param(
    [string]$Path = ""
)

$ErrorActionPreference = "Stop"

$interopPath = "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll"
$constPath = "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\api\redist\SolidWorks.Interop.swconst.dll"

if (-not (Test-Path $interopPath)) {
    throw "SOLIDWORKS interop assembly not found: $interopPath"
}

if (-not (Test-Path $constPath)) {
    throw "SOLIDWORKS constants assembly not found: $constPath"
}

[void][System.Reflection.Assembly]::LoadFrom($interopPath)
[void][System.Reflection.Assembly]::LoadFrom($constPath)

if (-not ("SolidWorksAnnotationInspector" -as [type])) {
    Add-Type -ReferencedAssemblies @($interopPath, $constPath) -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public static class SolidWorksAnnotationInspector
{
    public static string[] Inspect()
    {
        var results = new List<string>();
        ISldWorks app = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        ModelDoc2 model = app.IActiveDoc2;
        if (model == null)
        {
            return new[] { "NO_ACTIVE_DOCUMENT" };
        }

        results.Add("TITLE=" + model.GetTitle());
        results.Add("PATH=" + model.GetPathName());
        results.Add("DISPLAY_DIMENSION_TYPE=" + (int)swAnnotationType_e.swDisplayDimension);

        object current = model.GetFirstAnnotation2();
        int index = 0;
        while (current != null && index < 60)
        {
            Annotation annotation = current as Annotation;
            if (annotation == null)
            {
                results.Add("ANNOTATION[" + index + "]=<null-cast>");
                break;
            }

            int typeCode = annotation.GetType();
            object specific = null;
            string specificType = "<null>";
            string dimensionName = string.Empty;
            try
            {
                specific = annotation.GetSpecificAnnotation();
                specificType = specific == null ? "<null>" : specific.GetType().FullName;

                DisplayDimension displayDimension = specific as DisplayDimension;
                if (displayDimension != null)
                {
                    dimensionName = displayDimension.GetNameForSelection();
                }
            }
            catch (Exception ex)
            {
                specificType = "<error:" + ex.GetType().Name + ">";
            }

            results.Add(
                "ANNOTATION[" + index + "] type=" + typeCode +
                " specific=" + specificType +
                " name=" + dimensionName);

            current = annotation.GetNext3();
            index++;
        }

        return results.ToArray();
    }
}
"@
}

if (-not [string]::IsNullOrWhiteSpace($Path)) {
    & powershell.exe -NoProfile -File "C:\SW_plugin\scripts\setup\open-sample-document.ps1" -Path $Path | Out-Null
    Start-Sleep -Seconds 6
}

[SolidWorksAnnotationInspector]::Inspect() | ForEach-Object { Write-Output $_ }
