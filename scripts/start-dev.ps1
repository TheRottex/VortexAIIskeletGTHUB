$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runDir = Join-Path $root 'artifacts\dev-run'
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$pidFile = Join-Path $runDir 'pids.json'

function Start-VortexProcess {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Project,

        [string[]]$Args = @()
    )

    if (-not (Test-Path $Project)) {
        throw "$Name proje dosyası bulunamadı: $Project"
    }

    $log = Join-Path $runDir "$Name.log"
    $errorLog = Join-Path $runDir "$Name.err.log"

    Remove-Item $log, $errorLog -Force -ErrorAction SilentlyContinue

    # Proje yolundaki boşlukların dotnet tarafından parçalanmasını engeller.
    $escapedProject = '"' + $Project + '"'

    $argumentList = @(
        'run'
        '--project'
        $escapedProject
    ) + $Args

    Write-Host "$Name başlatılıyor..." -ForegroundColor Cyan

    $process = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList $argumentList `
        -WorkingDirectory $root `
        -PassThru `
        -RedirectStandardOutput $log `
        -RedirectStandardError $errorLog

    [pscustomobject]@{
        Name     = $Name
        Id       = $process.Id
        Log      = $log
        ErrorLog = $errorLog
    }
}

function Wait-Health {
    param(
        [Parameter(Mandatory)]
        [string]$Url,

        [Parameter(Mandatory)]
        [string]$Name
    )

    Write-Host "$Name sağlık kontrolü bekleniyor: $Url" -ForegroundColor Yellow

    for ($i = 1; $i -le 60; $i++) {
        try {
            $response = Invoke-WebRequest `
                -Uri $Url `
                -UseBasicParsing `
                -TimeoutSec 2

            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                Write-Host "$Name hazır." -ForegroundColor Green
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    throw "$Name health endpoint hazır olmadı: $Url"
}

try {
    $pids = @()

    $pids += Start-VortexProcess `
        -Name 'server' `
        -Project (Join-Path $root 'Vortex.Server\Vortex.Server.csproj') `
        -Args @('--urls', 'http://127.0.0.1:5000')

    $pids += Start-VortexProcess `
        -Name 'web' `
        -Project (Join-Path $root 'Vortex.Web\Vortex.Web.csproj') `
        -Args @('--urls', 'http://127.0.0.1:5080')

    $pids += Start-VortexProcess `
        -Name 'local-agent' `
        -Project (Join-Path $root 'Vortex.LocalAgent\Vortex.LocalAgent.csproj')

    $pids | ConvertTo-Json | Set-Content -Path $pidFile -Encoding UTF8

    Wait-Health `
        -Url 'http://127.0.0.1:5000/health' `
        -Name 'Vortex.Server'

    Wait-Health `
        -Url 'http://127.0.0.1:5080/health' `
        -Name 'Vortex.Web'

    Wait-Health `
        -Url 'http://127.0.0.1:47891/health' `
        -Name 'Vortex.LocalAgent'

    $desktop = Start-VortexProcess `
        -Name 'desktop' `
        -Project (Join-Path $root 'Vortex.Desktop\Vortex.Desktop.csproj')

    $pids += $desktop
    $pids | ConvertTo-Json | Set-Content -Path $pidFile -Encoding UTF8

    Write-Host ''
    Write-Host 'Vortex development services started.' -ForegroundColor Green
    Write-Host 'Web: http://127.0.0.1:5080'
    Write-Host 'Server: http://127.0.0.1:5000/health'
    Write-Host 'Local Agent: http://127.0.0.1:47891/health'
    Write-Host 'Durdurmak için: .\scripts\stop-dev.ps1'
}
catch {
    Write-Host ''
    Write-Host "Başlatma hatası: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Log klasörü: $runDir" -ForegroundColor Yellow

    Get-ChildItem "$runDir\*.err.log" -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Host "`n========== $($_.Name) ==========" -ForegroundColor Red
            Get-Content $_.FullName -Tail 50
        }

    exit 1
}