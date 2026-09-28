$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '../IngaCal/wwwroot/lib'
$assets = @{
 'fullcalendar/calendar.js' = 'https://cdn.jsdelivr.net/npm/fullcalendar@7.1.0/all/global.js'
 'fullcalendar/skeleton.css' = 'https://cdn.jsdelivr.net/npm/fullcalendar@7.1.0/skeleton.css'
 'fullcalendar/bootstrap5.js' = 'https://cdn.jsdelivr.net/npm/@fullcalendar/bootstrap5@7.1.0/global.js'
 'fullcalendar/bootstrap5.css' = 'https://cdn.jsdelivr.net/npm/@fullcalendar/bootstrap5@7.1.0/theme.css'
 'fullcalendar/LICENSE.md' = 'https://cdn.jsdelivr.net/npm/fullcalendar@7.1.0/LICENSE.md'
 'bootstrap-icons/bootstrap-icons.css' = 'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.13.1/font/bootstrap-icons.css'
 'bootstrap-icons/fonts/bootstrap-icons.woff2' = 'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.13.1/font/fonts/bootstrap-icons.woff2'
 'bootstrap-icons/fonts/bootstrap-icons.woff' = 'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.13.1/font/fonts/bootstrap-icons.woff'
 'bootstrap-icons/LICENSE' = 'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.13.1/LICENSE'
 'chartjs/chart.umd.js' = 'https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.js'
 'chartjs/LICENSE.md' = 'https://cdn.jsdelivr.net/npm/chart.js@4.4.1/LICENSE.md'
 'chartjs/chartjs-plugin-datalabels.min.js' = 'https://cdn.jsdelivr.net/npm/chartjs-plugin-datalabels@2.2.0/dist/chartjs-plugin-datalabels.min.js'
 'chartjs/LICENSE-datalabels.md' = 'https://cdn.jsdelivr.net/npm/chartjs-plugin-datalabels@2.2.0/LICENSE.md'
}
foreach ($asset in $assets.GetEnumerator()) {
    $destination = Join-Path $root $asset.Key
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Invoke-WebRequest -Uri $asset.Value -OutFile $destination
    Write-Output $asset.Key
}
