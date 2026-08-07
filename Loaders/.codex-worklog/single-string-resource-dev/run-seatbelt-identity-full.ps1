param(
    [Parameter(Mandatory = $true)]
    [string]$RequestedRunRoot
)

$ErrorActionPreference = 'Stop'

$loaderExe = 'E:\Documents\GitHub\Loaders\Loaders\bin\x64\Release\Loaders.exe'
$sourceRoot = 'E:\Documents\GitHub\Seatbelt_orig\Seatbelt'
$vcvars = 'C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat'
$oldProjectGuid = '{AEC32155-D589-4150-8FE7-2900DF4554C8}'

function Get-ConflictSafePath {
    param([string]$Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full)) {
        return $full
    }

    $index = 1
    do {
        $candidate = $full + '_' + $index.ToString('00')
        $index++
    } while (Test-Path -LiteralPath $candidate)

    return $candidate
}

function Invoke-TimedCommand {
    param([Parameter(Mandatory = $true)][scriptblock]$Command)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & $Command 2>&1
    $exitCode = $LASTEXITCODE
    $sw.Stop()

    return [PSCustomObject]@{
        ExitCode = $exitCode
        DurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 3)
        Output = @($output | ForEach-Object { $_.ToString() })
    }
}

function Get-ProjectProperty {
    param([xml]$ProjectXml, [string]$Name)

    $node = $ProjectXml.Project.PropertyGroup |
        ForEach-Object { $_.$Name } |
        Where-Object { $_ -ne $null } |
        Select-Object -First 1

    if ($node -is [System.Xml.XmlElement]) {
        return $node.InnerText
    }

    return [string]$node
}

function Get-AssemblyProbe {
    param([string]$AssemblyPath)

    if (-not (Test-Path -LiteralPath $AssemblyPath)) {
        return [PSCustomObject]@{
            Exists = $false
            Name = $null
            StringResources = $null
            Error = 'missing assembly'
        }
    }

    try {
        $assembly = [Reflection.Assembly]::LoadFile($AssemblyPath)
        return [PSCustomObject]@{
            Exists = $true
            Name = $assembly.GetName().Name
            StringResources = @($assembly.GetManifestResourceNames() | Where-Object { $_ -like '__m.*' }).Count
            Error = $null
        }
    }
    catch {
        return [PSCustomObject]@{
            Exists = $true
            Name = $null
            StringResources = $null
            Error = $_.Exception.Message
        }
    }
}

function Find-OldNamespaceHits {
    param([string]$RunRoot)

    if (-not (Test-Path -LiteralPath $RunRoot)) {
        return @()
    }

    $hits = New-Object System.Collections.Generic.List[string]
    $files = Get-ChildItem -LiteralPath $RunRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

    foreach ($file in $files) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        $relative = [System.IO.Path]::GetRelativePath($RunRoot, $file.FullName)
        if ($text -match '(?m)^\s*namespace\s+Seatbelt(\b|\.)') {
            $hits.Add($relative + ':namespace')
        }
        if ($text -match '(?m)^\s*using\s+(static\s+)?Seatbelt(\b|\.)') {
            $hits.Add($relative + ':using')
        }
        if ($text -match '(?<![A-Za-z0-9_])Seatbelt\.Program\b') {
            $hits.Add($relative + ':qualified-program')
        }
    }

    return @($hits)
}

$runRoot = Get-ConflictSafePath -Path $RequestedRunRoot
$outsideLog = $runRoot + '.protector.log'
$commandLog = $runRoot + '.commands.log'
$protectCommand = "& '$loaderExe' --source '$sourceRoot' --output '$runRoot'"
$protectCommand | Set-Content -LiteralPath $commandLog -Encoding UTF8

$protector = Invoke-TimedCommand -Command {
    & $loaderExe --source $sourceRoot --output $runRoot
}
$protector.Output | Set-Content -LiteralPath $outsideLog -Encoding UTF8

New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$logsDir = Join-Path $runRoot 'logs'
New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
$protector.Output | Set-Content -LiteralPath (Join-Path $logsDir 'protector-run.log') -Encoding UTF8
$protectCommand | Add-Content -LiteralPath (Join-Path $logsDir 'commands.log') -Encoding UTF8

$projectPath = $null
$newAssembly = $null
$rootNamespace = $null
$projectGuid = $null
$startupObject = $null
$oldProjectMetadata = $null
$namespaceHits = @()
$buildExit = $null
$buildSeconds = $null
$directRunExit = $null
$directRunSeconds = $null
$msbuildRunExit = $null
$msbuildRunSeconds = $null
$directProbe = $null
$msbuildProbe = $null

if ($protector.ExitCode -eq 0) {
    $projects = @(Get-ChildItem -LiteralPath $runRoot -File -Filter '*.csproj')
    if ($projects.Count -eq 1) {
        $projectPath = $projects[0].FullName
        [xml]$projectXml = Get-Content -LiteralPath $projectPath
        $projectGuid = Get-ProjectProperty -ProjectXml $projectXml -Name 'ProjectGuid'
        $rootNamespace = Get-ProjectProperty -ProjectXml $projectXml -Name 'RootNamespace'
        $newAssembly = Get-ProjectProperty -ProjectXml $projectXml -Name 'AssemblyName'
        $startupObject = Get-ProjectProperty -ProjectXml $projectXml -Name 'StartupObject'
        $oldProjectMetadata = [string]::Equals($projectGuid, $oldProjectGuid, [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($rootNamespace, 'Seatbelt', [StringComparison]::Ordinal) -or
            [string]::Equals($newAssembly, 'Seatbelt', [StringComparison]::Ordinal) -or
            ($startupObject -like 'Seatbelt.*') -or
            [string]::Equals([System.IO.Path]::GetFileName($projectPath), 'Seatbelt.csproj', [StringComparison]::OrdinalIgnoreCase)
        $namespaceHits = @(Find-OldNamespaceHits -RunRoot $runRoot)

        $buildCommand = "`"$vcvars`" && msbuild `"$projectPath`" /m /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo"
        "cmd.exe /c $buildCommand" | Add-Content -LiteralPath (Join-Path $logsDir 'commands.log') -Encoding UTF8
        $build = Invoke-TimedCommand -Command {
            & cmd.exe /c $buildCommand
        }
        $build.Output | Set-Content -LiteralPath (Join-Path $logsDir 'generated-msbuild.log') -Encoding UTF8
        $buildExit = $build.ExitCode
        $buildSeconds = $build.DurationSeconds

        $directExe = Join-Path $runRoot ($newAssembly + '.exe')
        $msbuildExe = Join-Path $runRoot ('bin\Release\' + $newAssembly + '.exe')
        $directProbe = Get-AssemblyProbe -AssemblyPath $directExe
        $msbuildProbe = Get-AssemblyProbe -AssemblyPath $msbuildExe

        if (Test-Path -LiteralPath $directExe) {
            $directRun = Invoke-TimedCommand -Command {
                & $directExe --help
            }
            $directRun.Output | Set-Content -LiteralPath (Join-Path $logsDir 'direct-help.log') -Encoding UTF8
            $directRunExit = $directRun.ExitCode
            $directRunSeconds = $directRun.DurationSeconds
        }

        if (Test-Path -LiteralPath $msbuildExe) {
            $msbuildRun = Invoke-TimedCommand -Command {
                & $msbuildExe --help
            }
            $msbuildRun.Output | Set-Content -LiteralPath (Join-Path $logsDir 'msbuild-help.log') -Encoding UTF8
            $msbuildRunExit = $msbuildRun.ExitCode
            $msbuildRunSeconds = $msbuildRun.DurationSeconds
        }
    }
}

$pass = $protector.ExitCode -eq 0 -and
    $buildExit -eq 0 -and
    $directRunExit -eq 0 -and
    $msbuildRunExit -eq 0 -and
    $directProbe -ne $null -and
    $msbuildProbe -ne $null -and
    $directProbe.Exists -and
    $msbuildProbe.Exists -and
    [string]::Equals($directProbe.Name, $newAssembly, [StringComparison]::Ordinal) -and
    [string]::Equals($msbuildProbe.Name, $newAssembly, [StringComparison]::Ordinal) -and
    $directProbe.StringResources -eq 1 -and
    $msbuildProbe.StringResources -eq 1 -and
    $oldProjectMetadata -eq $false -and
    $namespaceHits.Count -eq 0

$summary = [PSCustomObject]@{
    Pass = [bool]$pass
    RunRoot = $runRoot
    GeneratedProject = $projectPath
    NewAssemblyName = $newAssembly
    NewRootNamespace = $rootNamespace
    ProjectGuid = $projectGuid
    StartupObject = $startupObject
    HasOldProjectMetadata = $oldProjectMetadata
    OldNamespaceHits = @($namespaceHits)
    ProtectorExitCode = $protector.ExitCode
    ProtectorSeconds = $protector.DurationSeconds
    MsBuildExitCode = $buildExit
    MsBuildSeconds = $buildSeconds
    DirectRunExitCode = $directRunExit
    DirectRunSeconds = $directRunSeconds
    MsBuildRunExitCode = $msbuildRunExit
    MsBuildRunSeconds = $msbuildRunSeconds
    DirectAssemblyProbe = $directProbe
    MsBuildAssemblyProbe = $msbuildProbe
    ProtectorLog = Join-Path $logsDir 'protector-run.log'
    MsBuildLog = Join-Path $logsDir 'generated-msbuild.log'
}

$summaryPath = Join-Path $runRoot 'full-default-summary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
$summary | ConvertTo-Json -Depth 8

if (-not $pass) {
    exit 1
}
