<#
    pack-nexus.ps1 - builds the Nexus Mods upload archive for Lethal Gargoyles.

    The Release build runs this automatically (see the PackNexus target in
    Plugin\LethalGargoyles.csproj), right alongside the Thunderstore package. Run it by hand
    only when you want to repack without rebuilding:

        powershell -ExecutionPolicy Bypass -File .\pack-nexus.ps1 -Version 0.8.0

    It reads from Plugin\bin\Release\ and does NOT compile anything, so a hand run packages
    whatever the last Release build left there.

    WHY THE LAYOUT IS WHAT IT IS
    ----------------------------
    Thunderstore packages start at `plugins\` because r2modman and Gale know to graft that
    onto BepInEx. Nexus has no such convention: Vortex and a player with a mouse both expect
    the archive to mirror the GAME FOLDER. So the root of this zip holds `BepInEx\`, and
    dragging that one folder onto the game directory is a complete manual install.

    `fomod\` sits beside it for Vortex and Mod Organizer 2, which read it and offer the
    Coroner file as a tick box. Managers that ignore fomod fall back to the root layout and
    still land correctly - that redundancy is on purpose.
#>
[CmdletBinding()]
param(
    # Normally passed by MSBuild from the csproj <Version>. Stamped into fomod\info.xml and
    # into the zip's file name.
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # Which bin\ folder to package from. Nexus releases come from Release, always.
    [string]$Configuration = 'Release',

    # Repo root. Defaults to two levels up from this script (Plugin\Nexus\ -> repo root).
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }

$nexusDir = $PSScriptRoot
$binDir   = Join-Path $RepoRoot "Plugin\bin\$Configuration\netstandard2.1"
$stage    = Join-Path $nexusDir 'obj\stage'
$outDir   = Join-Path $nexusDir 'Packages'
$zipPath  = Join-Path $outDir "LethalGargoyles-$Version.zip"

# Everything the archive needs, and where it comes from. Checked up front so a missing input
# fails with the path that is wrong instead of producing a quietly incomplete zip.
$srcDll      = Join-Path $binDir 'DropDaDeuce.LethalGargoyles.dll'
$srcOriginal = Join-Path $binDir 'DropDaDeuce.LethalGargoyles_original.dll'
$srcNVorbis  = Join-Path $binDir 'NVorbis.dll'
$srcNVorbXml = Join-Path $binDir 'NVorbis.xml'
$srcBundle   = Join-Path $RepoRoot 'UnityProject\AssetBundles\StandaloneWindows\gargoyleassets'
$srcVoice    = Join-Path $RepoRoot 'Plugin\Thunderstore\Voice Lines'
$srcCoroner  = Join-Path $RepoRoot 'Plugin\Thunderstore\BepInEx\config\EliteMasterEric-Coroner\Strings_en-us_gargoyle.xml'
$srcIcon     = Join-Path $RepoRoot 'icon.png'
$srcChanges  = Join-Path $RepoRoot 'CHANGELOG.md'
$srcManual   = Join-Path $nexusDir 'MANUAL-INSTALL.txt'
$srcInfo     = Join-Path $nexusDir 'fomod\info.xml'
$srcModCfg   = Join-Path $nexusDir 'fomod\ModuleConfig.xml'
$srcSchema   = Join-Path $nexusDir 'schema\XmlScript5.0.xsd'

$required = @($srcDll, $srcNVorbis, $srcNVorbXml, $srcBundle, $srcVoice, $srcCoroner,
              $srcIcon, $srcChanges, $srcManual, $srcInfo, $srcModCfg, $srcSchema)
$missing = $required | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missing) {
    throw "pack-nexus: missing input(s):`n  " + ($missing -join "`n  ")
}

# The netcode patcher rewrites the DLL in place and leaves the pre-patch copy beside it. No
# _original.dll means the patch never ran, and every RPC in the mod throws a reflection error
# at runtime for every player who installs it. Worth stopping the pack over.
if (-not (Test-Path -LiteralPath $srcOriginal)) {
    throw "pack-nexus: '$srcOriginal' is missing, so the netcode patcher did not run on this Release build. Rebuild before packaging - shipping an unpatched DLL breaks every RPC in the mod."
}

Write-Host "pack-nexus: staging Lethal Gargoyles $Version from $Configuration"

# Stage from scratch every time. Reusing the folder is how a file deleted from the mod keeps
# shipping for another three releases.
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$pluginDir = Join-Path $stage 'BepInEx\plugins\LethalGargoyles'
$libDir    = Join-Path $pluginDir 'lib\NVorbis'
$coronDir  = Join-Path $stage 'Optional\Coroner\BepInEx\config\EliteMasterEric-Coroner'
$fomodDir  = Join-Path $stage 'fomod'
foreach ($d in @($pluginDir, $libDir, $coronDir, $fomodDir)) {
    New-Item -ItemType Directory -Path $d -Force | Out-Null
}

# --- BepInEx\plugins\LethalGargoyles\ : mirrors the Thunderstore layout one level down -----
Copy-Item -LiteralPath $srcDll    -Destination $pluginDir
Copy-Item -LiteralPath $srcBundle -Destination $pluginDir
Copy-Item -LiteralPath $srcNVorbis  -Destination $libDir
Copy-Item -LiteralPath $srcNVorbXml -Destination $libDir
Copy-Item -LiteralPath $srcVoice -Destination (Join-Path $pluginDir 'Voice Lines') -Recurse

# --- Optional\ : offered as a tick box by the FOMOD, ignored by a manual drag -------------
Copy-Item -LiteralPath $srcCoroner -Destination $coronDir

# --- fomod\ : the guided installer Vortex and MO2 read ------------------------------------
(Get-Content -LiteralPath $srcInfo -Raw).Replace('@VERSION@', $Version) |
    Set-Content -LiteralPath (Join-Path $fomodDir 'info.xml') -Encoding utf8 -NoNewline
Copy-Item -LiteralPath $srcModCfg -Destination $fomodDir
Copy-Item -LiteralPath $srcIcon   -Destination (Join-Path $fomodDir 'icon.png')

# --- archive root : what a player sees first when they open the zip -----------------------
Copy-Item -LiteralPath $srcChanges -Destination $stage
Copy-Item -LiteralPath $srcManual  -Destination (Join-Path $stage 'READ ME FIRST - Manual Install.txt')

# --- FOMOD validation : both halves of what Vortex actually does --------------------------
# b28 shipped a FOMOD that Vortex rejected, and NEITHER of these checks alone would have
# caught it. Schema validation passed, because pointing a validator at the 5.0 schema
# bypasses the version detection that was the actual bug. So run the detection too.
$stagedModCfg = Join-Path $fomodDir 'ModuleConfig.xml'
$modCfgText   = Get-Content -LiteralPath $stagedModCfg -Raw

# 1. Version detection, byte-for-byte the regex from Nexus-Mods/fomod-installer
#    (XmlScriptType.cs, RegexVersion). Singleline only - deliberately NOT IgnoreCase, because
#    the real one is not either, and a check more lenient than the tool it models is useless.
$rxVersion = New-Object System.Text.RegularExpressions.Regex(
    'xsi:noNamespaceSchemaLocation="[^"]*((XmlScript)|(ModConfig))(.*?).xsd',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
# Vortex takes the FIRST match in the file, so a second mention of the attribute name - in a
# comment, say - is a decoy that can capture the match and hand back a garbage version. b31's
# own warning comment quoted the regex verbatim and missed doing this by one character.
$attrCount = ([regex]::Matches($modCfgText, [regex]::Escape('noNamespaceSchemaLocation='))).Count
if ($attrCount -ne 1) {
    throw "pack-nexus: fomod\ModuleConfig.xml mentions noNamespaceSchemaLocation $attrCount times, expected exactly 1. Vortex matches the FIRST occurrence in raw text, so any extra mention - even inside a comment - can hijack version detection. Remove it."
}

$m = $rxVersion.Match($modCfgText)
$detected = if ($m.Success -and $m.Groups[4].Value) { $m.Groups[4].Value } else { '1.0' }
if ($detected -ne '5.0') {
    throw "pack-nexus: Vortex would read this FOMOD as XmlScript $detected, not 5.0. The xsi:noNamespaceSchemaLocation string in fomod\ModuleConfig.xml is wrong - check the casing of 'ModConfig', the 'xsi:' prefix, and that there is no whitespace around the '='. At 1.0 the schema predates moduleImage and installSteps, and Vortex rejects the installer with a misleading line number."
}

# 2. Schema validation against the version detection just resolved to.
$vset = New-Object System.Xml.XmlReaderSettings
$vset.ValidationType = [System.Xml.ValidationType]::Schema
$null = $vset.Schemas.Add($null, $srcSchema)
$vErrors = New-Object System.Collections.ArrayList
$vset.add_ValidationEventHandler([System.Xml.Schema.ValidationEventHandler] {
    param($s, $e)
    if ($e.Severity -eq [System.Xml.Schema.XmlSeverityType]::Error) {
        [void]$vErrors.Add("Line $($e.Exception.LineNumber), Pos $($e.Exception.LinePosition): $($e.Message)")
    }
})
$vr = [System.Xml.XmlReader]::Create($stagedModCfg, $vset)
try { while ($vr.Read()) { } }
catch { [void]$vErrors.Add("fatal: $($_.Exception.Message)") }
finally { $vr.Close() }
if ($vErrors.Count -gt 0) {
    throw "pack-nexus: fomod\ModuleConfig.xml fails XmlScript 5.0 validation:`n  " + ($vErrors -join "`n  ")
}
Write-Host "pack-nexus: fomod validates as XmlScript $detected"

New-Item -ItemType Directory -Path $outDir -Force | Out-Null
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

# CreateFromDirectory rather than Compress-Archive. Both write spec-correct forward-slash
# entry names on this machine (measured 2026-08-16 with an independent zip reader), but
# Compress-Archive has a long history of emitting backslash separators on Windows PowerShell
# 5.1, and this archive goes to strangers with unknown extractors. The trailing $false means
# "do not nest everything under a folder named after the staging directory".
#
# Do NOT try to verify separators by reading ZipArchiveEntry.FullName back - on .NET Framework
# that property rewrites '/' to the platform separator as it reads, so a correct archive looks
# broken and a broken one looks correct. It cost half an hour once already.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

# Guard against a silently incomplete archive - a mistyped staging path copies nothing and
# throws nothing, and the failure only shows up as a mod that will not load.
$staged = (Get-ChildItem -LiteralPath $stage -Recurse -File).Count
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try { $packed = @($zip.Entries | Where-Object { $_.Length -gt 0 -or $_.Name }).Count }
finally { $zip.Dispose() }
if ($packed -lt $staged) {
    throw "pack-nexus: staged $staged files but the archive holds $packed. Do not upload it."
}

$sizeMb = '{0:N1}' -f ((Get-Item -LiteralPath $zipPath).Length / 1MB)
$clips  = (Get-ChildItem -LiteralPath (Join-Path $pluginDir 'Voice Lines') -Recurse -Filter *.ogg).Count
Write-Host "pack-nexus: wrote $zipPath ($sizeMb MB, $clips voice clips)"
