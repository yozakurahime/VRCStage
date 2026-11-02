$ErrorActionPreference = 'Stop'
$labRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$handRoot = Join-Path $PSScriptRoot 'HandTracking'
$handPython = Join-Path $handRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $handPython)) { & python -m venv (Join-Path $handRoot '.venv'); if ($LASTEXITCODE) { throw 'Python 3.10+ necessário para compilar a ponte.' } }
& $handPython -m pip install -r (Join-Path $handRoot 'requirements.txt')
if ($LASTEXITCODE) { throw 'Falha ao instalar dependências da ponte.' }
$handModels = Join-Path $handRoot 'models'
if (-not (Test-Path -LiteralPath (Join-Path $handModels 'hand_landmarker.task'))) { throw 'Modelos ausentes; veja HandTracking\MODEL-SOURCE.txt.' }
Push-Location $labRoot
try {
    & $handPython -m PyInstaller --noconfirm --clean --onedir --name 'AvatarLab Webcam' --distpath Build\WebcamBridge --workpath TestOutput\WebcamBuild --specpath TestOutput --collect-all mediapipe --copy-metadata mediapipe --add-data ($handModels+';models') (Join-Path $handRoot 'webcam_bridge.py')
    if ($LASTEXITCODE) { throw 'Falha ao compilar a ponte webcam.' }
    $noticeRoot = Join-Path $labRoot 'Build\WebcamBridge\AvatarLab Webcam\Licencas'
    New-Item -ItemType Directory -Force -Path $noticeRoot | Out-Null
    foreach ($distribution in Get-ChildItem -LiteralPath (Join-Path $handRoot '.venv\Lib\site-packages') -Directory -Filter '*.dist-info') {
        $notices = @(Get-ChildItem -LiteralPath $distribution.FullName -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE)' })
        foreach ($notice in $notices) {
            $relative = $notice.FullName.Substring($distribution.FullName.Length+1)
            $target = Join-Path $noticeRoot (Join-Path $distribution.Name $relative)
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            Copy-Item -LiteralPath $notice.FullName -Destination $target
        }
    }
    $pythonLicense = Join-Path (Split-Path (Get-Command python).Source) 'LICENSE.txt'
    if (Test-Path -LiteralPath $pythonLicense) { Copy-Item -LiteralPath $pythonLicense -Destination (Join-Path $noticeRoot 'Python-LICENSE.txt') }
    Copy-Item -LiteralPath (Join-Path $handRoot 'MODEL-SOURCE.txt') -Destination $noticeRoot
    Copy-Item -LiteralPath (Join-Path $handRoot 'MEDIAPIPE-LICENSE.txt') -Destination $noticeRoot
    Copy-Item -LiteralPath (Join-Path $handRoot 'IFACIAL-V3-LICENSE.txt'),(Join-Path $handRoot 'IFACIAL-V3-SOURCE.txt') -Destination $noticeRoot
} finally { Pop-Location }
