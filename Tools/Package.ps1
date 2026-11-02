param([string]$Destination)
$ErrorActionPreference='Stop'
$labRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$player=Join-Path $labRoot 'Build\AvatarLabOffline'
$version=[regex]::Match([IO.File]::ReadAllText((Join-Path $labRoot 'Assets\AvatarLab\Runtime\StageBrand.cs')),'Version="([^"]+)"').Groups[1].Value
if(-not $version) {throw 'Version unavailable.'}
if(-not $Destination) {$Destination=Join-Path $labRoot ('Build\VRCStage-'+$version+'-Windows.zip')}
$stage=Join-Path $labRoot ('Build\Public-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
# Explicit allowlist: never copy a player's logs, settings or test results.
foreach($file in @('UnityPlayer.dll','UnityCrashHandler64.exe')) {Copy-Item -LiteralPath (Join-Path $player $file) -Destination $stage}
Copy-Item -LiteralPath (Join-Path $player 'AvatarLab Offline.exe') -Destination (Join-Path $stage 'VRCStage.exe')
Copy-Item -LiteralPath (Join-Path $player 'AvatarLab Offline_Data') -Destination (Join-Path $stage 'VRCStage_Data') -Recurse
Copy-Item -LiteralPath (Join-Path $player 'MonoBleedingEdge') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $labRoot 'Build\AvatarLab-Exportador.unitypackage') -Destination (Join-Path $stage 'VRCStage-Exporter.unitypackage')
New-Item -ItemType Directory -Path (Join-Path $stage 'Exemplos') | Out-Null
foreach($sample in @('Robot.alavatar','PhysicsChain.alavatar','Studio.alworld')) {Copy-Item -LiteralPath (Join-Path $player ('Exemplos\'+$sample)) -Destination (Join-Path $stage 'Exemplos')}
foreach($name in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','ROADMAP.md','CONTRIBUTING.md','SECURITY.md','docs','Licencas')) {Copy-Item -LiteralPath (Join-Path $labRoot $name) -Destination $stage -Recurse}
New-Item -ItemType Directory -Path (Join-Path $stage 'Tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $labRoot 'Tools\AllowIfacial.ps1') -Destination (Join-Path $stage 'Tools')
foreach($entry in @(@('WebcamBridge\AvatarLab Webcam','HandTracking'),@('RGBMotionBridge\AvatarLab RGB Motion','MocapBridge'))) {
    $bridge=Join-Path $labRoot ('Build\'+$entry[0])
    if(Test-Path -LiteralPath $bridge) {Copy-Item -LiteralPath $bridge -Destination (Join-Path $stage $entry[1]) -Recurse}
    else {Write-Warning ('Optional bridge not built: '+$entry[0])}
}
foreach($entry in @(@('Assets\ThirdParty\UnityCharacterMotion','UnityCharacterMotion'),@('Assets\ThirdParty\Ultraleap','Ultraleap'),@('Assets\ThirdParty\OpenVR','OpenVR'),@('Tools\HandTracking','MediaPipe'))) {
    $notice=Join-Path $stage ('Licencas\'+$entry[1]);New-Item -ItemType Directory -Force -Path $notice | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $labRoot $entry[0]) -File | Where-Object {$_.Name -match '(LICENSE|SOURCE)\.(txt|md)$'} | Copy-Item -Destination $notice
}
Copy-Item -LiteralPath (Join-Path $labRoot 'Packages\jp.keijiro.klak.spout\LICENSE') -Destination (Join-Path $stage 'Licencas\KlakSpout-LICENSE.txt')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $Destination -CompressionLevel Optimal -Force
Get-FileHash -LiteralPath $Destination -Algorithm SHA256
Write-Host ('Public stage: '+$stage)
