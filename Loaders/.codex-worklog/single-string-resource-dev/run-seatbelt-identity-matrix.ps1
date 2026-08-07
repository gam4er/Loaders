param(
    [Parameter(Mandatory = $true)]
    [string]$BaseRoot,

    [string[]]$Strategies = @(
        'XorBase64',
        'LcgBase64',
        'GZipBase64',
        'GZipLcgBase64',
        'HexReverseXor',
        'DecimalDelta',
        'Utf16DeltaArrays',
        'ShuffledUtf16Triplets',
        'InterleavedMaskPairs',
        'AffineBase64',
        'BytePermutation',
        'UInt64Packing',
        'GuidPacking',
        'BigIntegerPacking',
        'JunkedBase64'
    )
)

$ErrorActionPreference = 'Stop'

$loaderExe = 'E:\Documents\GitHub\Loaders\Loaders\bin\x64\Release\Loaders.exe'
$sourceRoot = 'E:\Documents\GitHub\Seatbelt_orig\Seatbelt'
$vcvars = 'C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat'
$oldProjectGuid = '{AEC32155-D589-4150-8FE7-2900DF4554C8}'

function Resolve-RunBase {
    param([string]$RequestedRoot)

    $root = [System.IO.Path]::GetFullPath($RequestedRoot)
    $marker = Join-Path $root 'matrix-run.id'

    if ((Test-Path -LiteralPath $root) -and -not (Test-Path -LiteralPath $marker)) {
        $suffix = 1
        do {
            $candidate = $root + '_' + $suffix.ToString('00')
            $candidateMarker = Join-Path $candidate 'matrix-run.id'
            $suffix++
        } while ((Test-Path -LiteralPath $candidate) -and -not (Test-Path -LiteralPath $candidateMarker))

        $root = $candidate
        $marker = $candidateMarker
    }

    New-Item -ItemType Directory -Path $root -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'logs') -Force | Out-Null
    if (-not (Test-Path -LiteralPath $marker)) {
        [Guid]::NewGuid().ToString('D') | Set-Content -LiteralPath $marker -Encoding UTF8
    }

    return $root
}

function Invoke-TimedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

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
    param(
        [xml]$ProjectXml,
        [string]$Name
    )

    $node = $ProjectXml.Project.PropertyGroup |
        ForEach-Object { $_.$Name } |
        Where-Object { $_ -ne $null } |
        Select-Object -First 1

    if ($node -is [System.Xml.XmlElement]) {
        return $node.InnerText
    }

    return [string]$node
}

function Test-GeneratedProjectMetadata {
    param(
        [string]$ProjectPath,
        [string]$NewAssemblyName
    )

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath
    $projectGuid = Get-ProjectProperty -ProjectXml $projectXml -Name 'ProjectGuid'
    $rootNamespace = Get-ProjectProperty -ProjectXml $projectXml -Name 'RootNamespace'
    $assemblyName = Get-ProjectProperty -ProjectXml $projectXml -Name 'AssemblyName'
    $startupObject = Get-ProjectProperty -ProjectXml $projectXml -Name 'StartupObject'

    $projectMetadataOld = $false
    $projectMetadataOld = $projectMetadataOld -or [string]::Equals($projectGuid, $oldProjectGuid, [StringComparison]::OrdinalIgnoreCase)
    $projectMetadataOld = $projectMetadataOld -or [string]::Equals($rootNamespace, 'Seatbelt', [StringComparison]::Ordinal)
    $projectMetadataOld = $projectMetadataOld -or [string]::Equals($assemblyName, 'Seatbelt', [StringComparison]::Ordinal)
    $projectMetadataOld = $projectMetadataOld -or ($startupObject -like 'Seatbelt.*')
    $projectMetadataOld = $projectMetadataOld -or [string]::Equals([System.IO.Path]::GetFileName($ProjectPath), 'Seatbelt.csproj', [StringComparison]::OrdinalIgnoreCase)
    $projectMetadataOld = $projectMetadataOld -or (-not [string]::Equals($assemblyName, $NewAssemblyName, [StringComparison]::Ordinal))

    return [PSCustomObject]@{
        ProjectGuid = $projectGuid
        RootNamespace = $rootNamespace
        AssemblyName = $assemblyName
        StartupObject = $startupObject
        HasOldProjectMetadata = $projectMetadataOld
    }
}

function Find-OldNamespaceHits {
    param([string]$RunRoot)

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

function Find-CopiedSourceArtifacts {
    param([string]$RunRoot)

    if (-not (Test-Path -LiteralPath $RunRoot)) {
        return @()
    }

    $hits = New-Object System.Collections.Generic.List[string]
    $items = Get-ChildItem -LiteralPath $RunRoot -Force -Recurse
    foreach ($item in $items) {
        $relative = [System.IO.Path]::GetRelativePath($RunRoot, $item.FullName)
        $segments = $relative -split '[\\/]'
        $directoryHit = $false
        foreach ($segment in $segments) {
            if ($segment -in @('.vs', '.git', '.idea', '.vscode', 'TestResults')) {
                $directoryHit = $true
                break
            }
        }

        if ($directoryHit) {
            $hits.Add($relative)
            continue
        }

        if (-not $item.PSIsContainer) {
            $name = $item.Name
            if ($name.EndsWith('.user', [StringComparison]::OrdinalIgnoreCase) -or
                $name.EndsWith('.suo', [StringComparison]::OrdinalIgnoreCase) -or
                $name.EndsWith('.cache', [StringComparison]::OrdinalIgnoreCase) -or
                $name.EndsWith('.dtbcache.json', [StringComparison]::OrdinalIgnoreCase) -or
                $name.EndsWith('.csproj.user', [StringComparison]::OrdinalIgnoreCase)) {
                $hits.Add($relative)
            }
        }
    }

    return @($hits)
}

function Test-AssemblyInfoIdentity {
    param([string]$RunRoot)

    $oldHits = New-Object System.Collections.Generic.List[string]
    $files = Get-ChildItem -LiteralPath $RunRoot -Recurse -File -Filter 'AssemblyInfo.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

    foreach ($file in $files) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        $relative = [System.IO.Path]::GetRelativePath($RunRoot, $file.FullName)
        if ($text -match 'AssemblyTitle\("SB"\)' -or
            $text -match 'AssemblyProduct\("SB"\)' -or
            $text -match '897dee3f-be2e-4030-9f07-2f2561a0d7fb' -or
            $text -match 'Copyright ©  2018') {
            $oldHits.Add($relative)
        }
    }

    return @($oldHits)
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

function Load-ExistingResults {
    param([string]$SummaryPath)

    if (-not (Test-Path -LiteralPath $SummaryPath)) {
        return @()
    }

    $json = Get-Content -LiteralPath $SummaryPath -Raw
    if ([string]::IsNullOrWhiteSpace($json)) {
        return @()
    }

    return @($json | ConvertFrom-Json)
}

$baseRoot = Resolve-RunBase -RequestedRoot $BaseRoot
$summaryPath = Join-Path $baseRoot 'matrix-summary.json'
$commandLogPath = Join-Path $baseRoot 'logs\commands.log'
$existingResults = @(Load-ExistingResults -SummaryPath $summaryPath)
$resultsByStrategy = @{}
foreach ($result in $existingResults) {
    $resultsByStrategy[$result.Strategy] = $result
}

foreach ($strategy in $Strategies) {
    $runRoot = Join-Path $baseRoot $strategy
    $baseLogs = Join-Path $baseRoot 'logs'
    $protectBaseLog = Join-Path $baseLogs ($strategy + '.protector.log')
    $buildBaseLog = Join-Path $baseLogs ($strategy + '.msbuild.log')

    $protectCommand = "& '$loaderExe' --source '$sourceRoot' --output '$runRoot' --skip-symbol-renaming --string-obfuscation-strategy $strategy"
    Add-Content -LiteralPath $commandLogPath -Encoding UTF8 -Value $protectCommand
    $protector = Invoke-TimedCommand -Command {
        & $loaderExe --source $sourceRoot --output $runRoot --skip-symbol-renaming --string-obfuscation-strategy $strategy
    }
    $protector.Output | Set-Content -LiteralPath $protectBaseLog -Encoding UTF8

    $runLogs = Join-Path $runRoot 'logs'
    if (Test-Path -LiteralPath $runRoot) {
        New-Item -ItemType Directory -Path $runLogs -Force | Out-Null
        $protector.Output | Set-Content -LiteralPath (Join-Path $runLogs 'protector-run.log') -Encoding UTF8
    }

    $copiedSourceArtifacts = @(Find-CopiedSourceArtifacts -RunRoot $runRoot)

    $projectPath = $null
    $projectMetadata = $null
    $newAssembly = $null
    $build = $null
    $buildExit = $null
    $buildDuration = $null
    $directRunExit = $null
    $directRunDuration = $null
    $msbuildRunExit = $null
    $msbuildRunDuration = $null
    $directProbe = $null
    $msbuildProbe = $null
    $namespaceHits = @()
    $assemblyInfoHits = @()

    if ($protector.ExitCode -eq 0 -and (Test-Path -LiteralPath $runRoot)) {
        $projects = @(Get-ChildItem -LiteralPath $runRoot -File -Filter '*.csproj')
        if ($projects.Count -eq 1) {
            $projectPath = $projects[0].FullName
            [xml]$projectXml = Get-Content -LiteralPath $projectPath
            $newAssembly = Get-ProjectProperty -ProjectXml $projectXml -Name 'AssemblyName'
            $projectMetadata = Test-GeneratedProjectMetadata -ProjectPath $projectPath -NewAssemblyName $newAssembly
            $namespaceHits = @(Find-OldNamespaceHits -RunRoot $runRoot)
            $assemblyInfoHits = @(Test-AssemblyInfoIdentity -RunRoot $runRoot)

            $buildCommand = "`"$vcvars`" && msbuild `"$projectPath`" /m /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo"
            Add-Content -LiteralPath $commandLogPath -Encoding UTF8 -Value ("cmd.exe /c " + $buildCommand)
            $build = Invoke-TimedCommand -Command {
                & cmd.exe /c $buildCommand
            }
            $build.Output | Set-Content -LiteralPath $buildBaseLog -Encoding UTF8
            $buildExit = $build.ExitCode
            $buildDuration = $build.DurationSeconds

            if (Test-Path -LiteralPath $runLogs) {
                $build.Output | Set-Content -LiteralPath (Join-Path $runLogs 'generated-msbuild.log') -Encoding UTF8
            }

            $directExe = Join-Path $runRoot ($newAssembly + '.exe')
            $msbuildExe = Join-Path $runRoot ('bin\Release\' + $newAssembly + '.exe')
            $directProbe = Get-AssemblyProbe -AssemblyPath $directExe
            $msbuildProbe = Get-AssemblyProbe -AssemblyPath $msbuildExe

            if (Test-Path -LiteralPath $directExe) {
                $directRun = Invoke-TimedCommand -Command {
                    & $directExe --help
                }
                $directRun.Output | Set-Content -LiteralPath (Join-Path $baseLogs ($strategy + '.direct-help.log')) -Encoding UTF8
                if (Test-Path -LiteralPath $runLogs) {
                    $directRun.Output | Set-Content -LiteralPath (Join-Path $runLogs 'direct-help.log') -Encoding UTF8
                }
                $directRunExit = $directRun.ExitCode
                $directRunDuration = $directRun.DurationSeconds
            }

            if (Test-Path -LiteralPath $msbuildExe) {
                $msbuildRun = Invoke-TimedCommand -Command {
                    & $msbuildExe --help
                }
                $msbuildRun.Output | Set-Content -LiteralPath (Join-Path $baseLogs ($strategy + '.msbuild-help.log')) -Encoding UTF8
                if (Test-Path -LiteralPath $runLogs) {
                    $msbuildRun.Output | Set-Content -LiteralPath (Join-Path $runLogs 'msbuild-help.log') -Encoding UTF8
                }
                $msbuildRunExit = $msbuildRun.ExitCode
                $msbuildRunDuration = $msbuildRun.DurationSeconds
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
        $projectMetadata -ne $null -and
        -not $projectMetadata.HasOldProjectMetadata -and
        $namespaceHits.Count -eq 0 -and
        $assemblyInfoHits.Count -eq 0 -and
        $copiedSourceArtifacts.Count -eq 0

    $summary = [PSCustomObject]@{
        Strategy = $strategy
        Pass = [bool]$pass
        RunRoot = $runRoot
        GeneratedProject = $projectPath
        NewAssemblyName = $newAssembly
        ProjectMetadata = $projectMetadata
        ProtectorExitCode = $protector.ExitCode
        ProtectorSeconds = $protector.DurationSeconds
        MsBuildExitCode = $buildExit
        MsBuildSeconds = $buildDuration
        DirectRunExitCode = $directRunExit
        DirectRunSeconds = $directRunDuration
        MsBuildRunExitCode = $msbuildRunExit
        MsBuildRunSeconds = $msbuildRunDuration
        DirectAssemblyProbe = $directProbe
        MsBuildAssemblyProbe = $msbuildProbe
        OldNamespaceHits = @($namespaceHits)
        OldAssemblyInfoHits = @($assemblyInfoHits)
        CopiedSourceArtifactHitsBeforeBuild = @($copiedSourceArtifacts)
        ProtectorLog = $protectBaseLog
        MsBuildLog = $buildBaseLog
    }

    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'strategy-summary.json') -Encoding UTF8
    $resultsByStrategy[$strategy] = $summary
    @($resultsByStrategy.Values) | Sort-Object Strategy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

    Write-Host ("{0}: pass={1}, protector={2}, msbuild={3}, direct={4}, msbuildRun={5}, assembly={6}" -f $strategy, $pass, $protector.ExitCode, $buildExit, $directRunExit, $msbuildRunExit, $newAssembly)
}

$finalResults = @($resultsByStrategy.Values) | Sort-Object Strategy
$failed = @($finalResults | Where-Object { -not $_.Pass })
$final = [PSCustomObject]@{
    BaseRoot = $baseRoot
    StrategyCount = $finalResults.Count
    Passed = @($finalResults | Where-Object { $_.Pass }).Count
    Failed = $failed.Count
    FailedStrategies = @($failed | ForEach-Object { $_.Strategy })
    SummaryPath = $summaryPath
    CommandLog = $commandLogPath
}

$final | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $baseRoot 'matrix-final-summary.json') -Encoding UTF8
$final | ConvertTo-Json -Depth 5

if ($failed.Count -gt 0) {
    exit 1
}
