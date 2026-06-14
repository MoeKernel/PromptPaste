param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$Project = Join-Path $Root 'PromptPaste/PromptPaste.csproj'
$ProjectDir = Split-Path $Project -Parent
$FinalOutput = Join-Path $Root 'bin'
$DistOutput = Join-Path $Root 'dist'
$InstallerScript = Join-Path $Root 'installer/PromptPaste.nsi'
$WorkDir = Join-Path $Root '.build'
$TempPublish = Join-Path $WorkDir 'publish'
$TempObj = Join-Path $ProjectDir 'obj'
$TempBin = Join-Path $WorkDir 'bin'
$BackupOutput = Join-Path $Root 'bin.__backup__'
$DeployOutput = 'D:\Protable\PromptPaste'
$BuildLogDir = Join-Path $Root 'docs/log'
$BuildSuccessCounterFile = Join-Path $BuildLogDir 'build-success-count.txt'

function Get-NsisCompiler {
    $command = Get-Command 'makensis.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        'C:\Program Files (x86)\NSIS\makensis.exe',
        'C:\Program Files\NSIS\makensis.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content $ProjectPath
    $version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($version)) {
        return '2.0.0'
    }

    return $version
}

function New-NsisInstaller {
    param(
        [string]$SourceDir,
        [string]$OutputDir,
        [string]$ScriptPath,
        [string]$Version
    )

    $makensis = Get-NsisCompiler
    if ($makensis -eq $null) {
        throw "NSIS compiler not found. Install NSIS from https://nsis.sourceforge.io/Download, or add makensis.exe to PATH."
    }

    if (-not (Test-Path $ScriptPath)) {
        throw "NSIS script not found: $ScriptPath"
    }

    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }

    $outputFile = Join-Path $OutputDir 'PromptPaste-Setup.exe'
    if (Test-Path $outputFile) {
        Remove-Item $outputFile -Force
    }

    $args = @(
        '/V4',
        '/INPUTCHARSET',
        'UTF8',
        "/DAPP_VERSION=$Version",
        "/DPUBLISH_DIR=$SourceDir",
        "/DDIST_DIR=$OutputDir",
        $ScriptPath
    )

    Push-Location (Split-Path $ScriptPath -Parent)
    try {
        $nsisOutput = & $makensis @args 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($nsisOutput) {
        $nsisOutput | ForEach-Object { Write-Host $_ }
    }

    if ($exitCode -ne 0) {
        $outputText = ($nsisOutput | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($outputText)) {
            throw "NSIS compiler failed with exit code $exitCode"
        }

        throw "NSIS compiler failed with exit code $exitCode`n$outputText"
    }

    if (-not (Test-Path $outputFile)) {
        throw "Installer was not created: $outputFile"
    }

    return $outputFile
}

function Update-BuildSuccessCounter {
    param(
        [string]$Path,
        [string]$Configuration,
        [string]$OutputPath
    )

    $count = 0
    if (Test-Path $Path) {
        $content = Get-Content $Path -Raw -ErrorAction SilentlyContinue
        if ($content -match 'BuildSuccessCount=(\d+)') {
            $count = [int]$Matches[1]
        }
        elseif (($content.Trim()) -match '^\d+$') {
            $count = [int]$content.Trim()
        }
    }

    $count++
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    @(
        "BuildSuccessCount=$count"
        "LastSuccessAt=$([DateTimeOffset]::Now.ToString('o'))"
        "LastConfiguration=$Configuration"
        "LastOutput=$OutputPath"
    ) | Set-Content -Path $Path -Encoding UTF8

    return $count
}

if (-not (Test-Path $Project)) {
    throw "Project file not found: $Project"
}

# Remove stale SDK-generated files from earlier builds.
# The final root bin/ is preserved until the new publish succeeds.
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

    Get-ChildItem $TempPublish -Recurse -Include *.pdb,*.xml -ErrorAction SilentlyContinue |
        Remove-Item -Force

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

    $deployed = $false
    try {
        Copy-Item -Path (Join-Path $FinalOutput '*') -Destination $DeployOutput -Recurse -Force
        $deployed = $true
    }
    catch {
        Write-Warning "Build succeeded, but deploy failed. Close PromptPaste if it is running and rerun the script. $($_.Exception.Message)"
    }

    $forbidden = @(
        (Join-Path $FinalOutput 'obj'),
        (Join-Path $FinalOutput '.codegraph'),
        (Join-Path $FinalOutput 'src'),
        (Join-Path $FinalOutput 'main.py'),
        (Join-Path $FinalOutput 'requirements.txt'),
        (Join-Path $FinalOutput 'resources\styles')
    )
    foreach ($path in $forbidden) {
        if (Test-Path $path) {
            throw "Forbidden artifact found in publish output: $path"
        }
    }

    $symbols = Get-ChildItem $FinalOutput -Recurse -Include *.pdb,*.xml -ErrorAction SilentlyContinue
    if ($symbols) {
        throw "Debug/documentation symbols found in publish output: $($symbols[0].FullName)"
    }

    $projectVersion = Get-ProjectVersion -ProjectPath $Project
    $createdInstaller = New-NsisInstaller -SourceDir $FinalOutput -OutputDir $DistOutput -ScriptPath $InstallerScript -Version $projectVersion

    $buildSuccessCount = Update-BuildSuccessCounter -Path $BuildSuccessCounterFile -Configuration $Configuration -OutputPath $FinalOutput

    Write-Host "Build succeeded: $FinalOutput"
    Write-Host "Installer created: $createdInstaller"
    Write-Host "Build success count: $buildSuccessCount"
    if ($deployed) {
        Write-Host "Deployed to: $DeployOutput"
    }
}
finally {
    if (Test-Path $WorkDir) {
        try {
            Remove-Item $WorkDir -Recurse -Force
        }
        catch {
            Write-Warning "Could not remove temporary build directory: $WorkDir. $($_.Exception.Message)"
        }
    }
    if (Test-Path $ProjectObj) {
        Remove-Item $ProjectObj -Recurse -Force
    }
    if (Test-Path $ProjectBin) {
        Remove-Item $ProjectBin -Recurse -Force
    }
}
