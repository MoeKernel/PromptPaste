param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$Project = Join-Path $Root 'PromptPaste/PromptPaste.csproj'
$FinalOutput = Join-Path $Root 'bin'
$WorkDir = Join-Path $Root '.build'
$TempPublish = Join-Path $WorkDir 'publish'
$TempObj = Join-Path $WorkDir 'obj'
$TempBin = Join-Path $WorkDir 'bin'
$BackupOutput = Join-Path $Root 'bin.__backup__'
$DeployOutput = 'D:\Protable\PromptPaste'

if (-not (Test-Path $Project)) {
    throw "Project file not found: $Project"
}

# Remove stale SDK-generated files from earlier builds.
# The final root bin/ is preserved until the new publish succeeds.
$ProjectDir = Split-Path $Project -Parent
$ProjectObj = Join-Path $ProjectDir 'obj'
$ProjectBin = Join-Path $ProjectDir 'bin'

if (Test-Path $ProjectObj) {
    Remove-Item $ProjectObj -Recurse -Force
}

if (Test-Path $ProjectBin) {
    try {
        Remove-Item $ProjectBin -Recurse -Force
    }
    catch {
        Write-Warning "Could not remove old project bin directory, probably because a file is in use: $ProjectBin"
    }
}

if (Test-Path $WorkDir) {
    Remove-Item $WorkDir -Recurse -Force
}
New-Item -ItemType Directory -Path $TempPublish | Out-Null

try {
    dotnet publish $Project `
        --configuration $Configuration `
        --output $TempPublish `
        /p:BaseIntermediateOutputPath="$TempObj/" `
        /p:BaseOutputPath="$TempBin/" `
        /p:DebugType=None `
        /p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    if (Test-Path $BackupOutput) {
        Remove-Item $BackupOutput -Recurse -Force
    }

    if (Test-Path $FinalOutput) {
        Move-Item $FinalOutput $BackupOutput
    }

    try {
        Move-Item $TempPublish $FinalOutput
        if (Test-Path $BackupOutput) {
            Remove-Item $BackupOutput -Recurse -Force
        }
    }
    catch {
        if ((-not (Test-Path $FinalOutput)) -and (Test-Path $BackupOutput)) {
            Move-Item $BackupOutput $FinalOutput
        }
        throw
    }

    if (-not (Test-Path $DeployOutput)) {
        New-Item -ItemType Directory -Path $DeployOutput -Force | Out-Null
    }

    Copy-Item -Path (Join-Path $FinalOutput '*') -Destination $DeployOutput -Recurse -Force

    Write-Host "Build succeeded: $FinalOutput"
    Write-Host "Deployed to: $DeployOutput"
}
finally {
    if (Test-Path $WorkDir) {
        Remove-Item $WorkDir -Recurse -Force
    }
}
