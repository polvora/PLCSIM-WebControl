# build.ps1 - Compiles src\PlcsimAutoStart.cs into PlcsimAutoStart.exe (x64, .NET Framework 4).
#
# Requires only the .NET Framework 4.x C# compiler (csc.exe), which ships with Windows -
# no Visual Studio or .NET SDK needed. The Siemens PLCSIM Advanced API DLL is located
# automatically (it is proprietary and NOT shipped with this project).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $root "src\PlcsimAutoStart.cs"
$out  = Join-Path $root "PlcsimAutoStart.exe"

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe (.NET Framework 4) not found at $csc. Install the .NET Framework 4.x runtime (it is built into Windows Server 2016+)." }
if (-not (Test-Path $src)) { throw "Source not found: $src" }

# Locate the Siemens PLCSIM Advanced API DLL: env override first, then scan the install folders.
$api = $env:PLCSIM_API_DLL
if (-not $api -or -not (Test-Path $api)) {
    $searchRoots = @()
    if ($env:ProgramFiles)        { $searchRoots += (Join-Path $env:ProgramFiles "Siemens\Automation") }
    if (${env:ProgramFiles(x86)}) { $searchRoots += (Join-Path ${env:ProgramFiles(x86)} "Siemens\Automation") }
    $searchRoots = $searchRoots | Where-Object { Test-Path $_ }
    if ($searchRoots) {
        $api = Get-ChildItem -Path $searchRoots -Recurse -Filter "Siemens.Simatic.Simulation.Runtime.Api.x64.dll" -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $api) {
    throw "Could not find Siemens.Simatic.Simulation.Runtime.Api.x64.dll. Install S7-PLCSIM Advanced, or set the PLCSIM_API_DLL environment variable to its full path."
}
Write-Host "Using Siemens API DLL: $api"

$webext = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll"

Write-Host "Compiling -> $out (x64)..."
& $csc /nologo /platform:x64 /target:exe /optimize+ `
    "/reference:$api" "/reference:$webext" "/reference:System.Web.dll" "/reference:System.ServiceProcess.dll" `
    "/out:$out" $src
if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)." }
Write-Host "Build OK: $out" -ForegroundColor Green
