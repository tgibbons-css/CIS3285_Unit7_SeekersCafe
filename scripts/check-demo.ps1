param(
    [string]$WebUrl = 'http://localhost:5100',
    [string]$MenuUrl = 'http://localhost:5101',
    [string]$OrderUrl = 'http://localhost:5102',
    [string]$PaymentUrl = 'http://localhost:5103'
)

# Read-only preflight. Works with either local .NET processes or Docker containers.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(5)
$WebUrl = $WebUrl.TrimEnd('/'); $MenuUrl = $MenuUrl.TrimEnd('/')
$OrderUrl = $OrderUrl.TrimEnd('/'); $PaymentUrl = $PaymentUrl.TrimEnd('/')

function Read-Endpoint([string]$Url, [string]$Origin = '') {
    $request = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::Get), $Url
    if ($Origin) { $request.Headers.Add('Origin', $Origin) }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) { throw "$Url returned HTTP $([int]$response.StatusCode)." }
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $allowedOrigin = if ($response.Headers.Contains('Access-Control-Allow-Origin')) {
                @($response.Headers.GetValues('Access-Control-Allow-Origin'))[0]
            } else { '' }
            return @{ Text = $text; Origin = $allowedOrigin }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

try {
    foreach ($service in @(
        @{ Name = 'WebApp'; Url = "$WebUrl/health" },
        @{ Name = 'MenuService'; Url = "$MenuUrl/" },
        @{ Name = 'OrderService'; Url = "$OrderUrl/" },
        @{ Name = 'PaymentService'; Url = "$PaymentUrl/" }
    )) {
        $identity = (Read-Endpoint $service.Url).Text | ConvertFrom-Json
        if ($identity.service -ne $service.Name) { throw "Unexpected service at $($service.Url)." }
        Write-Output "READY: $($service.Name)"
    }
    $config = (Read-Endpoint "$WebUrl/api/config").Text | ConvertFrom-Json
    if ($config.menuBaseUrl -ne $MenuUrl -or $config.orderBaseUrl -ne $OrderUrl) {
        throw 'WebApp browser URLs do not match these preflight URLs. Check /api/config and host port settings.'
    }
    $page = (Read-Endpoint "$WebUrl/").Text
    if (-not $page.Contains('id="checkout-form"') -or -not $page.Contains('id="track-form"')) {
        throw 'The expected checkout/tracking interface was not served.'
    }
    $menuResponse = Read-Endpoint "$MenuUrl/api/menu" $WebUrl
    $orderResponse = Read-Endpoint "$OrderUrl/api/orders" $WebUrl
    if ($menuResponse.Origin -ne $WebUrl -or $orderResponse.Origin -ne $WebUrl) {
        throw 'The APIs do not allow this frontend origin. Check Frontend:Origin and use localhost.'
    }
    $menu = @($menuResponse.Text | ConvertFrom-Json)
    # Windows PowerShell and PowerShell 7 enumerate empty JSON arrays differently.
    $menu = @($menu | Where-Object { $null -ne $_ })
    if ($menu.Count -eq 0) { throw 'No available menu items. Add or enable a sample item before the happy-path demonstration.' }
    Write-Output "READY: $($menu.Count) available menu items; order database readable; frontend configuration and CORS match."
    Write-Output 'Preflight passed. No orders, payments, menu items or configuration were changed.'
    Write-Output 'Confirm normal payment simulation settings separately; this read-only check does not process a payment.'
} catch {
    throw "Demo preflight failed: $($_.Exception.Message) Start the four apps and check docs/classroom-demo.md."
} finally { $client.Dispose() }
