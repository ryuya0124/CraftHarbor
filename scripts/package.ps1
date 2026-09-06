param([string]$Version = '0.1.1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $artifactRoot = Join-Path $projectRoot 'artifacts'
    $packageRoot = Join-Path $artifactRoot 'packages'
    New-Item -ItemType Directory -Force $packageRoot | Out-Null
    foreach ($kind in @('portable', 'standalone')) {
        $target = Join-Path $artifactRoot $kind
        if ($kind -eq 'portable') {
            dotnet publish src/CraftHarbor.Desktop -c Release -r win-x64 --self-contained false -o $target
        } else {
            dotnet publish src/CraftHarbor.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $target
        }
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $kind" }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'LICENSE') -Destination $target -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $target -Recurse -Force
        $zipPath = Join-Path $packageRoot "CraftHarbor-$Version-win-x64-$kind.zip"
        Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zipPath -Force
    }
    $hashLines = Get-ChildItem -LiteralPath $packageRoot -Filter "CraftHarbor-$Version-*.zip" | ForEach-Object {
        $digest = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        '{0}  {1}' -f $digest.Hash.ToLowerInvariant(), $_.Name
    }
    $hashLines | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Encoding utf8
    Get-ChildItem -LiteralPath $packageRoot | Select-Object Name, Length
} finally { Pop-Location }
