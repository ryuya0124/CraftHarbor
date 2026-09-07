param([Parameter(Mandatory)][string]$Manifest)
$ErrorActionPreference = 'Stop'
$resultPath = $Manifest + '.result.json'
try {
    $request = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
    $parent = Get-Process -Id $request.ParentId -ErrorAction SilentlyContinue
    if ($parent -and $parent.StartTime.ToUniversalTime().Ticks -eq $request.ParentStartedTicks) {
        if (-not $parent.WaitForExit(120000)) { throw 'Application is still running; update was not applied.' }
    }
    if ((Get-Item -LiteralPath $request.Installer).Length -ne $request.Size -or (Get-FileHash -LiteralPath $request.Installer -Algorithm SHA256).Hash -ne $request.SHA256) { throw 'Installer verification failed.' }
    $target = [IO.Path]::GetFullPath($request.TargetDirectory)
    if (-not (Test-Path -LiteralPath (Join-Path $target 'unins000.exe'))) { throw 'Existing installation was not found.' }
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/DIR="{0}"' -f $target), ('/LOG="{0}"' -f ($Manifest + '.install.log')))
    $setup = Start-Process -FilePath $request.Installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Installer failed: $($setup.ExitCode)" }
    @{Status='success';Completed=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding utf8
    if ($request.Restart) { Start-Process -FilePath (Join-Path $target 'CraftHarbor.exe') -WindowStyle Normal }
} catch {
    @{Status='failed';Error=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding utf8
    exit 1
}
