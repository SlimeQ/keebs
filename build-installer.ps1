param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "Keebs\Keebs.csproj"
$publishDir = Join-Path $root "artifacts\publish\$Runtime"
$installerDir = Join-Path $root "artifacts\installer"
$wxs = Join-Path $root "Installer\Keebs.wxs"
$msi = Join-Path $installerDir "Keebs-Setup-$Runtime.msi"

New-Item -ItemType Directory -Force -Path $publishDir, $installerDir | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    /p:PublishDir="$publishDir\"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

dotnet wix build $wxs `
    -arch x64 `
    -d "ProjectDir=$root" `
    -d "PublishDir=$publishDir" `
    -out $msi

if ($LASTEXITCODE -ne 0) {
    throw "wix build failed with exit code $LASTEXITCODE"
}

Write-Host "Installer written to $msi"
