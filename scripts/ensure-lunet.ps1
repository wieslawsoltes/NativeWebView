$ErrorActionPreference = 'Stop'

$lunetVersion = if ($env:LUNET_VERSION) { $env:LUNET_VERSION } else { '1.0.10' }
$lunetFramework = if ($env:LUNET_FRAMEWORK) { $env:LUNET_FRAMEWORK } else { 'net10.0' }
$downloadUrl = "https://www.nuget.org/api/v2/package/lunet/$lunetVersion"

$globalPackagesLine = dotnet nuget locals global-packages --list |
    Select-String '^global-packages:\s*(.+)$' |
    Select-Object -First 1

if (-not $globalPackagesLine) {
    throw 'Unable to resolve the global NuGet packages directory.'
}

$globalPackagesDir = $globalPackagesLine.Matches[0].Groups[1].Value.Trim()
$packageRoot = Join-Path $globalPackagesDir "lunet/$lunetVersion"
$lunetDll = Join-Path $packageRoot "tools/$lunetFramework/any/lunet.dll"

if (Test-Path $lunetDll) {
    Write-Output $lunetDll
    return
}

$bootstrapDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $bootstrapDir | Out-Null

try {
    $packagePath = Join-Path $bootstrapDir "lunet.$lunetVersion.nupkg"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $packagePath

    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    Expand-Archive -Path $packagePath -DestinationPath $packageRoot -Force

    if (-not (Test-Path $lunetDll)) {
        throw "Failed to bootstrap Lunet runtime at $lunetDll."
    }

    Write-Output $lunetDll
}
finally {
    Remove-Item $bootstrapDir -Force -Recurse -ErrorAction SilentlyContinue
}
