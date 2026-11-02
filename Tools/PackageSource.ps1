param([string]$Destination)
$ErrorActionPreference='Stop'
$labRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(-not $Destination) {$Destination=Join-Path $labRoot 'Build\VRCStage-Source.zip'}
Push-Location $labRoot
try {
    & python Tools/check_public.py
    if($LASTEXITCODE) {throw 'Publication check failed.'}
    & git archive --format=zip --prefix=VRCStage/ --output=$Destination HEAD
    if($LASTEXITCODE) {throw 'Commit the reviewed public source before packaging.'}
    Get-FileHash -LiteralPath $Destination -Algorithm SHA256
} finally {Pop-Location}
