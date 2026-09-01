param ($Configuration, $TargetName, $ProjectDir, $TargetPath, $TargetDir)

Write-Host "Configuration: $Configuration"
Write-Host "Target file: $TargetPath"
Write-Host "Output dir: $TargetDir"

function CopyToFolder {
    param (
        [string]$SourceDir,
        [string]$DestinationDir
    )

    if (-not (Test-Path $SourceDir)) {
        Write-Host "Source not found: $SourceDir"
        return
    }

    if (-not (Test-Path $DestinationDir)) {
        New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
    }

    $failed = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -Path $SourceDir -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($SourceDir.Length).TrimStart('\')
        $targetPath = Join-Path $DestinationDir $relativePath
        $targetFolder = Split-Path $targetPath -Parent
        if (-not (Test-Path $targetFolder)) {
            New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
        }

        try {
            Copy-Item -Path $_.FullName -Destination $targetPath -Force -ErrorAction Stop
        }
        catch {
            $failed.Add($relativePath) | Out-Null
            Write-Host "Copy failed: $relativePath"
        }
    }

    if ($failed.Count -gt 0) {
        Write-Host "Some files were not copied. Close Navisworks and rebuild."
    }
    else {
        Write-Host "Copied to: $DestinationDir"
    }
}

$navisVersion = $Configuration.Replace("Debug", "").Replace("Release", "")

$addinMainFolder = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\SmartNavisTools.bundle"
New-Item -ItemType Directory -Path $addinMainFolder -Force | Out-Null
Copy-Item -Path (Join-Path $ProjectDir "PackageContents.xml") -Destination (Join-Path $addinMainFolder "PackageContents.xml") -Force

$addinFolder = Join-Path $addinMainFolder ("Contents\" + $navisVersion)
CopyToFolder -SourceDir $TargetDir -DestinationDir $addinFolder

$enUsFolder = Join-Path $addinFolder "en-US"
if (Test-Path $enUsFolder) {
    Get-ChildItem -Path $TargetDir -Filter *.ico -File | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $enUsFolder $_.Name) -Force -ErrorAction SilentlyContinue
    }
}
