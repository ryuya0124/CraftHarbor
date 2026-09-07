param([Parameter(Mandatory)][string]$Executable, [int]$Runs = 3)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name CraftHarbor -ErrorAction SilentlyContinue) { throw 'Close CraftHarbor before benchmarking; existing processes will not be touched.' }
$testRoot = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts\startup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$results = @()
for ($i = 0; $i -lt $Runs; $i++) {
    $info = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Executable))
    $info.UseShellExecute = $false
    $info.Environment['CRAFTHARBOR_DATA'] = Join-Path $testRoot "data-$i"
    # An empty extraction location exposes bundled native-runtime extraction cost.
    $info.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $testRoot "extract-$i"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($info)
    $first = $null; $ready = $null
    try {
        while ($watch.Elapsed.TotalSeconds -lt 20 -and -not $process.HasExited) {
            $process.Refresh()
            if ($null -eq $first -and $process.MainWindowHandle -ne [IntPtr]::Zero) { $first = $watch.ElapsedMilliseconds }
            if ($process.MainWindowTitle -like '*Minecraft Server Control*') { $ready = $watch.ElapsedMilliseconds; break }
            Start-Sleep -Milliseconds 10
        }
        if ($null -eq $ready) { throw 'Application did not reach main window within 20 seconds' }
        $results += [pscustomobject]@{Run=$i+1;FirstWindowMs=$first;MainWindowMs=$ready;Executable=$info.FileName}
    } finally {
        if (-not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) { throw "Test process $($process.Id) did not close normally" }
        }
        $process.Dispose()
    }
}
$results | ConvertTo-Json | Tee-Object -FilePath (Join-Path $testRoot 'results.json')
