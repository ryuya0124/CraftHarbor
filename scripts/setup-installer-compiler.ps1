param([string]$ToolsDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\tools'))
$ErrorActionPreference = 'Stop'
$ToolsDirectory = [IO.Path]::GetFullPath($ToolsDirectory)
$compilerDirectory = Join-Path $ToolsDirectory 'inno-7.1.0'
$compiler = Join-Path $compilerDirectory 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Directory -Force -Path $ToolsDirectory | Out-Null
    $setup = Join-Path $ToolsDirectory 'innosetup-7.1.0-x64.exe'
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $setup
    $expected = '0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f'
    if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Inno Setup download hash mismatch' }
    $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', '/TASKS=""', ('/DIR="{0}"' -f $compilerDirectory)) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw "Compiler setup failed: $($process.ExitCode)" }
}
Write-Output $compiler
