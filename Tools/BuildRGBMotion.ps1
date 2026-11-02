$ErrorActionPreference='Stop'
$labRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rgbPython=Join-Path $PSScriptRoot 'HandTracking\.venv\Scripts\python.exe'
if(-not (Test-Path -LiteralPath $rgbPython)) {throw 'Prepare a venv de Tools\HandTracking (NumPy e PyInstaller).'}
Push-Location $labRoot
try {
    & $rgbPython -m PyInstaller --noconfirm --clean --onedir --name 'AvatarLab RGB Motion' --distpath Build\RGBMotionBridge --workpath TestOutput\RGBMotionBuild --specpath TestOutput (Join-Path $PSScriptRoot 'RGBMotion\rgb_motion.py')
    if($LASTEXITCODE) {throw 'Falha ao compilar ponte RGB Motion.'}
    $notices=Join-Path $labRoot 'Build\RGBMotionBridge\AvatarLab RGB Motion\Licencas'
    New-Item -ItemType Directory -Force $notices | Out-Null
    foreach($distribution in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'HandTracking\.venv\Lib\site-packages') -Directory | Where-Object {$_.Name -match '^(numpy|pyinstaller).*dist-info$'}) {
        foreach($notice in Get-ChildItem -LiteralPath $distribution.FullName -Recurse -File | Where-Object {$_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE)'}) {
            $target=Join-Path $notices (Join-Path $distribution.Name ($notice.FullName.Substring($distribution.FullName.Length+1)))
            New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
            Copy-Item -LiteralPath $notice.FullName -Destination $target
        }
    }
    $base=(& $rgbPython -c 'import sys; print(sys.base_prefix)').Trim()
    if(Test-Path -LiteralPath (Join-Path $base 'LICENSE.txt')) {Copy-Item -LiteralPath (Join-Path $base 'LICENSE.txt') -Destination (Join-Path $notices 'Python-LICENSE.txt')}
} finally {Pop-Location}
