param(
    [string]$Dotnet = 'dotnet',
    [string]$AndroidSdkDirectory = $env:ANDROID_HOME,
    [string]$JavaSdkDirectory = $env:JAVA_HOME,
    [string]$ImagePackDirectory = (Join-Path $PSScriptRoot 'BundledData'),
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('apk','aab')][string]$Format = 'apk',
    [switch]$InstallDependencies
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/Juvis.Android/Juvis.Android.csproj'
$ImagePackDirectory = (Resolve-Path -LiteralPath $ImagePackDirectory).Path
$imageIndexPath = Join-Path $ImagePackDirectory 'image-index.json'
if (-not (Test-Path -LiteralPath $imageIndexPath -PathType Leaf)) { throw 'The image pack must contain image-index.json.' }
$imageIndex = Get-Content -LiteralPath $imageIndexPath -Raw | ConvertFrom-Json -AsHashtable
$imageRoot = [IO.Path]::GetFullPath((Join-Path $ImagePackDirectory 'Images')) + [IO.Path]::DirectorySeparatorChar
foreach ($relative in $imageIndex.Values) {
    $imagePath = [IO.Path]::GetFullPath((Join-Path $imageRoot $relative))
    if (-not $imagePath.StartsWith($imageRoot, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $imagePath -PathType Leaf)) {
        throw "Image pack entry is missing or outside Images: $relative"
    }
}
Write-Host "Building with $($imageIndex.Count) bundled images."
function Run-Dotnet([string[]]$Arguments) {
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
if ($InstallDependencies) {
    Run-Dotnet -Arguments @('workload','install','android')
    if (-not $AndroidSdkDirectory) { throw 'Specify -AndroidSdkDirectory for SDK installation.' }
    if (-not $JavaSdkDirectory) { throw 'Specify -JavaSdkDirectory for JDK installation.' }
    Write-Host 'Installing Android dependencies and accepting their SDK licenses.'
    Run-Dotnet -Arguments @('build',$project,'-t:InstallAndroidDependencies','-f','net10.0-android',"-p:AndroidSdkDirectory=$AndroidSdkDirectory", "-p:JavaSdkDirectory=$JavaSdkDirectory",'-p:AcceptAndroidSDKLicenses=True')
}
Run-Dotnet -Arguments @('run','--project',(Join-Path $PSScriptRoot 'tests/Juvis.Core.Tests.csproj'),'-c','Release')
$buildArgs = @('build',$project,'-t:Rebuild','-v:minimal','-c',$Configuration,"-p:AndroidPackageFormats=$Format",'-p:PublishTrimmed=false','-p:RunAOTCompilation=false',"-p:ImagePackDirectory=$ImagePackDirectory")
if ($AndroidSdkDirectory) { $buildArgs += "-p:AndroidSdkDirectory=$AndroidSdkDirectory" }
if ($JavaSdkDirectory) { $buildArgs += "-p:JavaSdkDirectory=$JavaSdkDirectory" }
Run-Dotnet -Arguments $buildArgs
$output = Join-Path $PSScriptRoot "src/Juvis.Android/bin/$Configuration/net10.0-android"
$destination = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Get-ChildItem -LiteralPath $output -Filter "*.$Format" | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destination -Force }
Write-Host "Build complete: $destination"
Write-Host 'These are development packages. Configure your own signing key before store distribution.'
