param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src/PeakReactionAudio/PeakReactionAudio.csproj'
& dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.json') -Raw | ConvertFrom-Json
$dll = Join-Path $projectRoot "src/PeakReactionAudio/bin/$Configuration/PeakReactionAudio.dll"
$version = $manifest.version_number
if ([System.Reflection.AssemblyName]::GetAssemblyName($dll).Version -ne [Version]::Parse("$version.0")) { throw 'Manifest and assembly versions differ.' }
$icon = Join-Path $projectRoot 'icon.png'
& (Join-Path $PSScriptRoot 'Generate-Icon.ps1') -OutputPath $icon
$artifacts = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$stage = [IO.Path]::GetFullPath((Join-Path $artifacts 'staging/lllei-PeakReactionAudio'))
# Keep recursive cleanup in this shell and verify the resolved workspace target.
if (-not $stage.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path.' }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$plugin = Join-Path $stage 'plugins/PeakReactionAudio'
New-Item -ItemType Directory -Path $plugin -Force | Out-Null
foreach ($event in @('PrePassOut','PullPlayer','BeingPulled','Falling','HardLanding','RegainConsciousness','Revived')) {
    New-Item -ItemType Directory -Path (Join-Path $plugin $event) -Force | Out-Null
}
Copy-Item -LiteralPath $dll -Destination $plugin
Copy-Item -LiteralPath (Join-Path $projectRoot 'Audio/PrePassOut/orga-dakara-yo.wav') -Destination (Join-Path $plugin 'PrePassOut')
Copy-Item -LiteralPath (Join-Path $projectRoot 'Audio/PrePassOut/goodnight-makka-pakka.mp3') -Destination (Join-Path $plugin 'PrePassOut')
Copy-Item -LiteralPath (Join-Path $projectRoot 'Audio/Falling/toms-screams.mp3') -Destination (Join-Path $plugin 'Falling')
foreach ($file in @('manifest.json','README.md','CHANGELOG.md','LICENSE','icon.png','config.example.cfg')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $stage
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $artifacts "lllei-PeakReactionAudio-$version.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
$writer = [IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($stagedFile in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $entryName = $stagedFile.FullName.Substring($stage.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer,$stagedFile.FullName,$entryName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    foreach ($event in @('PrePassOut','PullPlayer','BeingPulled','Falling','HardLanding','RegainConsciousness','Revived')) {
        $writer.CreateEntry("plugins/PeakReactionAudio/$event/") | Out-Null
    }
}
finally { $writer.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $dlls = @($zip.Entries | Where-Object { $_.FullName -match '\.dll$' })
    if ($dlls.Count -ne 1 -or $dlls[0].FullName.Replace('\','/') -ne 'plugins/PeakReactionAudio/PeakReactionAudio.dll') { throw 'Unexpected DLL in release package.' }
    foreach ($required in @('manifest.json','README.md','icon.png','config.example.cfg')) {
        if ($null -eq $zip.GetEntry($required)) { throw "Missing package entry $required" }
    }
}
finally { $zip.Dispose() }
Remove-Item -LiteralPath $stage -Recurse -Force
Write-Output "Created $archive"
Get-FileHash -LiteralPath $archive -Algorithm SHA256
