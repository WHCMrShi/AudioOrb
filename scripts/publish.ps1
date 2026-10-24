[CmdletBinding()]
param(
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'dotnet was not found. Install the .NET 8 SDK and add it to PATH.'
}

$out = Join-Path $root 'dist\AudioOrb-win-x64'
if (Test-Path $out) {
    Remove-Item $out -Recurse -Force
}

& $dotnet.Source publish .\src\AudioOrb\AudioOrb.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:WindowsAppSDKSelfContained=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $out
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

# Windows App SDK 1.8 pulls optional AI / WebView2 / Widgets payloads into
# self-contained output even when the app never references them. Removing the
# unused files keeps the portable folder around 160 MB instead of 208 MB.
$unusedPatterns = @(
    'onnxruntime*.dll',
    'DirectML.dll',
    'Microsoft.ML.OnnxRuntime.dll',
    'System.Numerics.Tensors.dll',
    'Microsoft.Windows.AI.*',
    'Microsoft.Graphics.Imaging*',
    'Microsoft.Windows.Workloads*',
    'Microsoft.Windows.Private*',
    'Microsoft.Windows.Vision*',
    'Microsoft.Windows.SemanticSearch*',
    'Microsoft.Windows.Internal.AI.*',
    'Microsoft.Windows.Internal.Vision.winmd',
    'SessionHandleIPCProxyStub.dll',
    'workloads*.json',
    'Microsoft.Windows.Widgets*',
    'Microsoft.Web.WebView2*'
)

Get-ChildItem $out -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        $name = $_.Name
        foreach ($pattern in $unusedPatterns) {
            if ($name -like $pattern) {
                return $true
            }
        }
        return $false
    } |
    Remove-Item -Force -ErrorAction SilentlyContinue

$npu = Join-Path $out 'NpuDetect'
if (Test-Path $npu) {
    Remove-Item $npu -Recurse -Force
}

$webViewLoader = Join-Path $out 'runtimes\win-x64\native\WebView2Loader.dll'
if (Test-Path $webViewLoader) {
    Remove-Item $webViewLoader -Force
}

Copy-Item (Join-Path $root 'README.md') $out -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'LICENSE') $out -ErrorAction SilentlyContinue

$exe = Join-Path $out 'AudioOrb.exe'
if (-not (Test-Path $exe)) {
    throw 'AudioOrb.exe is missing from the publish output.'
}

$totalMb = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Published: $out ($totalMb MB)" -ForegroundColor Green

if (-not $NoZip) {
    $zip = Join-Path $root 'dist\AudioOrb-win-x64.zip'
    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }

    Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal
    $zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Archive: $zip ($zipMb MB)" -ForegroundColor Green
}
