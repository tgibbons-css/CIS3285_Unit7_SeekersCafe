param([int]$WebPort = 5590, [int]$MenuPort = 5591, [int]$OrderPort = 5592, [int]$PaymentPort = 5593)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$workspace = Split-Path $PSScriptRoot -Parent
$composePath = Join-Path $workspace 'docker-compose.yml'
$projectName = 'seekers-qa-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$testRoot = Join-Path $workspace ".local/docker-tests/$projectName"
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(15)
$script:checks = 0
$script:composeCalls = 0
. (Join-Path $PSScriptRoot 'HttpTestHelpers.ps1')
$savedEnvironment = @{}
foreach ($key in @('WEB_PORT', 'MENU_PORT', 'ORDER_PORT', 'PAYMENT_PORT', 'SIMULATE_DECLINE', 'PAYMENT_DELAY_MS')) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
$env:WEB_PORT = "$WebPort"; $env:MENU_PORT = "$MenuPort"
$env:ORDER_PORT = "$OrderPort"; $env:PAYMENT_PORT = "$PaymentPort"
$env:SIMULATE_DECLINE = 'false'; $env:PAYMENT_DELAY_MS = '0'
$menuUrl = "http://localhost:$MenuPort"; $orderUrl = "http://localhost:$OrderPort"
$paymentUrl = "http://localhost:$PaymentPort"; $webUrl = "http://localhost:$WebPort"

function Invoke-Compose([string[]]$Arguments) {
    $script:composeCalls++
    $log = Join-Path $testRoot ("{0:D2}-compose.log" -f $script:composeCalls)
    # Docker writes normal build/progress output to stderr, which is not a PowerShell failure.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker compose --ansi never --progress plain --project-name $projectName -f $composePath @Arguments *> $log
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) { Get-Content -LiteralPath $log -Tail 45; throw "Compose failed: $($Arguments -join ' '). Log: $log" }
}

function Wait-Http([string]$Url) {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $response = $client.GetAsync($Url).GetAwaiter().GetResult()
            $ready = $response.IsSuccessStatusCode
            $response.Dispose()
            if ($ready) { return }
        } catch { }
        Start-Sleep -Milliseconds 300
    }
    throw "Application did not become ready: $Url"
}
function Wait-All {
    Wait-Http "$menuUrl/"; Wait-Http "$paymentUrl/"
    Wait-Http "$orderUrl/"; Wait-Http "$webUrl/health"
}
function Container-Id([string]$Service) {
    $value = & docker compose --project-name $projectName -f $composePath ps -q $Service
    if ($LASTEXITCODE -ne 0 -or -not $value) { throw "Container not found for $Service" }
    return $value.Trim()
}

try {
    foreach ($port in @($WebPort, $MenuPort, $OrderPort, $PaymentPort)) {
        $listener = New-Object Net.Sockets.TcpListener -ArgumentList ([Net.IPAddress]::Loopback), $port
        try { $listener.Start() } finally { $listener.Stop() }
    }
    $engine = & docker info --format '{{.OSType}}'
    Assert ($LASTEXITCODE -eq 0 -and $engine -eq 'linux') 'Start Docker Desktop with its Linux engine first.'
    Invoke-Compose @('config', '--quiet')
    Write-Output "Building four images for isolated project $projectName. Logs: $testRoot"
    Invoke-Compose @('build')
    Invoke-Compose @('up', '-d', '--no-build')
    Wait-All

    foreach ($service in @('menu', 'order', 'payment', 'web')) {
        $container = Container-Id $service
        $userId = & docker exec $container id -u
        Assert ($LASTEXITCODE -eq 0 -and [int]$userId -gt 0) "$service is not running as non-root."
        $mounts = & docker inspect --format '{{json .Mounts}}' $container | ConvertFrom-Json
        if ($service -eq 'web') { Assert (@($mounts).Count -eq 0) 'WebApp should not own a data volume.' }
        else {
            Assert (@($mounts).Count -eq 1 -and $mounts[0].Destination -eq '/data') "$service has unexpected mounts."
            Assert ($mounts[0].Name -eq "${projectName}_${service}-data") "$service does not exclusively own its volume."
        }
    }
    $config = (Send 'GET' "$webUrl/api/config").Data
    Assert ($config.menuBaseUrl -eq $menuUrl -and $config.orderBaseUrl -eq $orderUrl) 'Browser received internal Docker DNS names.'
    $html = $client.GetStringAsync("$webUrl/").GetAwaiter().GetResult()
    Assert ($html.Contains('id="checkout-form"')) 'Published frontend was not served.'
    foreach ($asset in @('styles.css', 'app.mjs', 'core.mjs')) {
        $response = $client.GetAsync("$webUrl/$asset").GetAwaiter().GetResult()
        try { Assert $response.IsSuccessStatusCode "Published asset missing: $asset" } finally { $response.Dispose() }
    }
    foreach ($url in @("$menuUrl/api/menu", "$orderUrl/api/orders")) {
        $cors = Send 'GET' $url $null 200 @{ Origin = $webUrl }
        Assert ($cors.Headers.GetValues('Access-Control-Allow-Origin') -contains $webUrl) 'Configured browser origin not allowed.'
    }
    Assert ((Send 'GET' "$menuUrl/api/menu").Data.Count -eq 7) 'Fresh menu volume was not seeded.'
    $body = @{ customerName = 'Container Demo'; paymentMethod = 'Dining Dollars'; items = @(@{ menuItemId = 1; quantity = 2 }, @{ menuItemId = 5; quantity = 1 }) }
    $checkout = (Send 'POST' "$orderUrl/api/orders" $body 201).Data
    $orderId = $checkout.order.orderId; $paymentId = $checkout.payment.paymentId
    Assert ($checkout.order.status -eq 'Paid' -and $checkout.order.totalAmount -eq 12) 'Docker DNS checkout failed.'
    $tea = (Send 'POST' "$menuUrl/api/menu" @{ name = 'Container Tea'; description = 'Persistence demo'; category = 'Other Drinks'; price = 2.95; isAvailable = $true } 201).Data
    Write-Output 'PASS: non-root processes, exclusive volumes, frontend assets/config/CORS, paid checkout over Docker DNS'

    Invoke-Compose @('up', '-d', '--no-build', '--force-recreate')
    Wait-All
    Assert ((Send 'GET' "$menuUrl/api/menu/$($tea.menuItemId)").Data.name -eq 'Container Tea') 'Recreation lost menu data.'
    Assert ((Send 'GET' "$orderUrl/api/orders/$orderId").Data.totalAmount -eq 12) 'Recreation lost order data.'
    Assert ((Send 'GET' "$paymentUrl/api/payments/$paymentId").Data.status -eq 'Succeeded') 'Recreation lost payment data.'
    Invoke-Compose @('down') # Named volumes intentionally survive.
    Invoke-Compose @('up', '-d', '--no-build')
    Wait-All
    Assert ((Send 'GET' "$menuUrl/api/menu").Data.Count -eq 8) 'Down/up reseeded or lost the edited menu.'
    Assert ((Send 'GET' "$orderUrl/api/orders/$orderId").Data.status -eq 'Paid') 'Down/up lost the paid order.'
    Assert ((Send 'GET' "$paymentUrl/api/payments/order/$orderId").Data.paymentId -eq $paymentId) 'Down/up lost payment identity.'
    Write-Output 'PASS: all three databases survive force-recreation and down/up'

    Invoke-Compose @('stop', 'menu')
    $null = Send 'GET' "$orderUrl/api/orders/$orderId"
    $null = Send 'GET' "$webUrl/health"
    $null = Send 'GET' "$paymentUrl/"
    $failure = Send 'POST' "$orderUrl/api/orders" $body 503
    Assert (-not $failure.Data.orderId) 'Menu outage should not create an order.'
    Invoke-Compose @('start', 'menu')
    Wait-Http "$menuUrl/"

    Invoke-Compose @('stop', 'payment')
    $unknown = (Send 'POST' "$orderUrl/api/orders" $body 503).Data
    Assert ($unknown.order.status -eq 'Pending' -and $unknown.paymentStatus -eq 'Unknown') 'Payment outage did not preserve Pending.'
    $null = Send 'GET' "$orderUrl/api/orders/$orderId"
    Invoke-Compose @('start', 'payment')
    Wait-Http "$paymentUrl/"
    $null = Send 'POST' "$orderUrl/api/orders/$($unknown.orderId)/payment/reconcile" $null 409
    Write-Output 'PASS: stop/start individual dependencies; existing order reads remain available'

    $menuContainer = Container-Id 'menu'; $orderContainer = Container-Id 'order'; $webContainer = Container-Id 'web'
    $env:SIMULATE_DECLINE = 'true'
    Invoke-Compose @('up', '-d', '--no-build', '--no-deps', 'payment')
    Wait-Http "$paymentUrl/"
    Assert ((Container-Id 'menu') -eq $menuContainer -and (Container-Id 'order') -eq $orderContainer -and (Container-Id 'web') -eq $webContainer) 'Payment reconfiguration recreated another service.'
    $declined = (Send 'POST' "$orderUrl/api/orders" $body 402).Data
    Assert ($declined.paymentStatus -eq 'Declined') 'Container decline configuration was not applied.'
    $null = Send 'PUT' "$orderUrl/api/orders/$($declined.orderId)/status" @{ status = 'Cancelled' }
    $env:SIMULATE_DECLINE = 'false'; $env:PAYMENT_DELAY_MS = '7000'
    Invoke-Compose @('up', '-d', '--no-build', '--no-deps', 'payment')
    Wait-Http "$paymentUrl/"
    $late = (Send 'POST' "$orderUrl/api/orders" $body 503).Data
    Assert ($late.paymentStatus -eq 'Unknown') 'Late container payment outcome should be unknown.'
    $saved = (Send 'GET' "$paymentUrl/api/payments/order/$($late.orderId)").Data
    Assert ($saved.status -eq 'Succeeded') 'Payment should commit before the response delay.'
    $reconciled = (Send 'POST' "$orderUrl/api/orders/$($late.orderId)/payment/reconcile").Data
    Assert ($reconciled.order.status -eq 'Paid' -and $reconciled.payment.paymentId -eq $saved.paymentId) 'Container reconciliation failed.'
    Write-Output 'PASS: payment-only reconfiguration, decline, committed-but-late payment and reconciliation'

    Invoke-Compose @('down')
    $env:PAYMENT_DELAY_MS = '0'
    Invoke-Compose @('up', '-d', '--no-build', '--no-deps', 'order', 'web')
    Wait-Http "$orderUrl/"; Wait-Http "$webUrl/health"
    Assert ((Send 'GET' "$orderUrl/api/orders/$orderId").Data.status -eq 'Paid') 'OrderService cannot independently read its own database.'
    $null = Send 'GET' "$webUrl/api/config"
    Write-Output "Passed $script:checks HTTP checks plus container ownership/network/persistence assertions. Logs: $testRoot"
} catch {
    try { Invoke-Compose @('logs', '--no-color', '--tail', '80') } catch { }
    throw
} finally {
    try {
        # Only the uniquely named, disposable project created by this script is removed.
        if ($projectName -notmatch '^seekers-qa-[a-f0-9]{10}$') { throw 'Unexpected test project name; refusing cleanup.' }
        Invoke-Compose @('down', '--volumes', '--remove-orphans', '--rmi', 'local')
    } finally {
        foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
        $client.Dispose()
    }
}
