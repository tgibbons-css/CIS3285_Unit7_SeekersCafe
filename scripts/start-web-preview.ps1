param([int]$WebPort = 5100, [int]$MenuPort = 5491, [int]$OrderPort = 5492, [int]$PaymentPort = 5493,
    [switch]$DeclinePayments, [int]$PaymentDelayMilliseconds = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$workspace = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $workspace ('.local/web-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(8)
$processes = @{}; $script:starts = 0; $script:checks = 0
. (Join-Path $PSScriptRoot 'HttpTestHelpers.ps1')
try {
    $menuConfig = '--ConnectionStrings:MenuDatabase "Data Source={0}"' -f (Join-Path $testRoot 'menu.db')
    $paymentConfig = '--ConnectionStrings:PaymentDatabase "Data Source={0}" --Simulation:DeclinePayments {1} --Simulation:ResponseDelayMilliseconds {2}' -f (Join-Path $testRoot 'payments.db'), $DeclinePayments.IsPresent.ToString().ToLowerInvariant(), $PaymentDelayMilliseconds
    $orderConfig = '--ConnectionStrings:OrderDatabase "Data Source={0}" --MenuService:BaseUrl http://localhost:{1}/ --PaymentService:BaseUrl http://localhost:{2}/ --PaymentService:TimeoutSeconds 2' -f (Join-Path $testRoot 'orders.db'), $MenuPort, $PaymentPort
    $webConfig = '--Services:MenuBaseUrl http://localhost:{0} --Services:OrderBaseUrl http://localhost:{1}' -f $MenuPort, $OrderPort
    Start-App 'MenuService' $MenuPort $menuConfig
    Start-App 'PaymentService' $PaymentPort $paymentConfig
    Start-App 'OrderService' $OrderPort $orderConfig
    Start-App 'WebApp' $WebPort $webConfig
    $baseUrl = "http://localhost:$WebPort"
    $html = $client.GetStringAsync("$baseUrl/").GetAwaiter().GetResult()
    Assert ($html.Contains('id="checkout-form"') -and $html.Contains('id="track-form"')) 'Frontend forms missing.'
    foreach ($asset in @('styles.css', 'app.mjs', 'core.mjs')) {
        $response = $client.GetAsync("$baseUrl/$asset").GetAwaiter().GetResult()
        try { Assert $response.IsSuccessStatusCode "Asset $asset did not load." } finally { $response.Dispose() }
    }
    $config = (Send 'GET' "$baseUrl/api/config").Data
    Assert ($config.menuBaseUrl -eq "http://localhost:$MenuPort" -and $config.orderBaseUrl -eq "http://localhost:$OrderPort") 'Browser API configuration did not use overrides.'
    $null = Send 'GET' "$baseUrl/health"
    $cors = Send 'GET' "http://localhost:$MenuPort/api/menu" $null 200 @{ Origin = $baseUrl }
    Assert ($cors.Headers.GetValues('Access-Control-Allow-Origin') -contains $baseUrl) 'Frontend origin is not allowed by CORS. Use port 5100.'
    $manifest = @{ url = $baseUrl; directory = $testRoot; menuPort = $MenuPort; orderPort = $OrderPort; paymentPort = $PaymentPort; services = @($processes.Keys | ForEach-Object { @{ name = $_; id = $processes[$_].Id; started = $processes[$_].StartTime.ToUniversalTime().ToString('o') } }) }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $testRoot 'processes.json') -Encoding utf8
    Write-Output "Preview ready: $baseUrl"
    Write-Output "Isolated data and process manifest: $testRoot"
    Write-Output "Stop with: ./scripts/stop-web-preview.ps1 -ManifestPath '$testRoot/processes.json'"
} catch {
    foreach ($name in @('WebApp', 'OrderService', 'MenuService', 'PaymentService')) { Stop-App $name }
    throw
} finally { $client.Dispose() }
