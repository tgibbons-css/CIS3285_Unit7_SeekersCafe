param([int]$Port = 5191)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$workspace = Split-Path $PSScriptRoot -Parent
$serviceRoot = Join-Path $workspace 'src/MenuService'
$dll = Join-Path $serviceRoot 'bin/Debug/net10.0/MenuService.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build the solution in Debug configuration first.' }

# Every run gets a separate real SQLite file; the classroom menu is never modified.
$testRoot = Join-Path $workspace ('.local/menu-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$database = Join-Path $testRoot 'menu.db'
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(5)
$baseUrl = "http://localhost:$Port"
$script:serviceProcess = $null
$script:startCount = 0
$script:checks = 0

function Assert($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Send([string]$Method, [string]$Path, $Body = $null, [int]$Expected = 200, $Headers = @{}) {
    $httpMethod = New-Object System.Net.Http.HttpMethod -ArgumentList $Method
    $request = New-Object System.Net.Http.HttpRequestMessage -ArgumentList $httpMethod, "$baseUrl$Path"
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { ConvertTo-Json -InputObject $Body -Depth 5 -Compress }
            $request.Content = New-Object System.Net.Http.StringContent -ArgumentList $json, ([Text.Encoding]::UTF8), 'application/json'
        }
        foreach ($key in $Headers.Keys) { $request.Headers.Add($key, [string]$Headers[$key]) }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Assert ([int]$response.StatusCode -eq $Expected) "$Method $Path expected $Expected, got $([int]$response.StatusCode): $text"
            $script:checks++
            $data = if ($text) { ConvertFrom-Json -InputObject $text } else { $null }
            if ($text -eq '[]') { $data = @() }
            return @{ Data = $data; Headers = $response.Headers; Text = $text }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

function Start-ServiceUnderTest {
    # Fail instead of accidentally running tests against an existing application.
    $listener = New-Object System.Net.Sockets.TcpListener -ArgumentList ([Net.IPAddress]::Loopback), $Port
    try { $listener.Start() } finally { $listener.Stop() }
    $script:startCount++
    $arguments = '"{0}" --urls {1} --contentRoot "{2}" --ConnectionStrings:MenuDatabase "Data Source={3}"' -f $dll, $baseUrl, $serviceRoot, $database
    $script:serviceProcess = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $testRoot "$script:startCount.stdout.log") `
        -RedirectStandardError (Join-Path $testRoot "$script:startCount.stderr.log")
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($script:serviceProcess.HasExited) { throw "MenuService exited. Inspect $testRoot" }
        try {
            $response = $client.GetAsync("$baseUrl/").GetAwaiter().GetResult()
            $ready = $response.IsSuccessStatusCode
            $response.Dispose()
            if ($ready) { return }
        } catch { }
        Start-Sleep -Milliseconds 200
    }
    throw 'MenuService startup timed out.'
}

function Stop-ServiceUnderTest {
    if ($null -ne $script:serviceProcess -and -not $script:serviceProcess.HasExited) {
        Stop-Process -Id $script:serviceProcess.Id
        $script:serviceProcess.WaitForExit()
    }
}

try {
    Start-ServiceUnderTest
    $items = (Send 'GET' '/api/menu').Data
    Assert ($items.Count -eq 7) 'Expected seven automatically seeded products.'
    Assert (@($items | Where-Object name -eq 'Latte')[0].price -eq 4.50) 'Seed prices changed.'
    Assert (Test-Path -LiteralPath $database) 'SQLite database was not created.'
    Write-Output 'PASS: fresh SQLite database and seven sample products'

    $body = @{ name = ' Demo Tea '; description = 'Test'; category = ' Other Drinks '; price = 2.95; isAvailable = $true }
    $created = Send 'POST' '/api/menu' $body 201
    $id = $created.Data.menuItemId
    Assert ($created.Headers.Location.ToString() -eq "/api/menu/$id") 'Incorrect Location header.'
    Assert ($created.Data.name -eq 'Demo Tea') 'Name was not trimmed.'
    Assert ((Send 'GET' "/api/menu/$id").Data.price -eq 2.95) 'Price did not round-trip exactly.'

    $body.price = 3.10
    $body.isAvailable = $false
    $updated = Send 'PUT' "/api/menu/$id" $body
    Assert ($updated.Data.price -eq 3.10 -and -not $updated.Data.isAvailable) 'Update failed.'
    Assert ((Send 'GET' '/api/menu').Data.Count -eq 7) 'Unavailable product was listed.'
    Assert ((Send 'GET' '/api/menu?includeUnavailable=true').Data.Count -eq 8) 'Unavailable product was omitted from full list.'
    Assert (-not (Send 'GET' "/api/menu/$id").Data.isAvailable) 'Unavailable item lookup failed.'
    Write-Output 'PASS: create, read, update, decimal prices, availability filtering'

    $null = Send 'POST' '/api/menu' @{} 400
    foreach ($price in @(-1, 1000.01, 1.005)) {
        $invalid = $body.Clone(); $invalid.price = $price
        $problem = Send 'POST' '/api/menu' $invalid 400
        Assert ($null -ne $problem.Data.errors.price) 'Expected price validation error.'
    }
    foreach ($field in @('name', 'description', 'category', 'price', 'isAvailable')) {
        $invalid = $body.Clone(); $invalid.Remove($field)
        $null = Send 'POST' '/api/menu' $invalid 400
    }
    foreach ($field in @('name', 'category')) {
        $invalid = $body.Clone(); $invalid[$field] = '   '
        $null = Send 'PUT' "/api/menu/$id" $invalid 400
    }
    foreach ($case in @(@{ field = 'name'; length = 101 }, @{ field = 'description'; length = 501 }, @{ field = 'category'; length = 51 })) {
        $invalid = $body.Clone(); $invalid[$case.field] = 'x' * $case.length
        $null = Send 'POST' '/api/menu' $invalid 400
    }
    $null = Send 'POST' '/api/menu' '{bad json' 400
    $null = Send 'POST' '/api/menu' 'null' 400
    $null = Send 'GET' '/api/menu?includeUnavailable=maybe' $null 400
    $null = Send 'GET' '/api/menu/2147483647' $null 404
    $null = Send 'PUT' '/api/menu/2147483647' $body 404
    $null = Send 'DELETE' '/api/menu/2147483647' $null 404
    Assert ((Send 'GET' '/api/menu?includeUnavailable=true').Data.Count -eq 8) 'Invalid requests changed the database.'
    Write-Output 'PASS: invalid fields, bounds, malformed JSON, missing items'

    $allowed = Send 'GET' '/api/menu' $null 200 @{ Origin = 'http://localhost:5100' }
    Assert ($allowed.Headers.GetValues('Access-Control-Allow-Origin') -contains 'http://localhost:5100') 'Local frontend CORS header missing.'
    $denied = Send 'GET' '/api/menu' $null 200 @{ Origin = 'https://example.org' }
    Assert (-not $denied.Headers.Contains('Access-Control-Allow-Origin')) 'Unexpected CORS origin allowed.'

    Stop-ServiceUnderTest
    Start-ServiceUnderTest
    Assert ((Send 'GET' '/api/menu?includeUnavailable=true').Data.Count -eq 8) 'Restart duplicated seed data.'
    Assert ((Send 'GET' "/api/menu/$id").Data.price -eq 3.10) 'Restart lost the update.'
    $null = Send 'DELETE' "/api/menu/$id" $null 204
    $null = Send 'GET' "/api/menu/$id" $null 404
    $null = Send 'DELETE' "/api/menu/$id" $null 404

    foreach ($item in (Send 'GET' '/api/menu?includeUnavailable=true').Data) {
        $null = Send 'DELETE' "/api/menu/$($item.menuItemId)" $null 204
    }
    Stop-ServiceUnderTest
    Start-ServiceUnderTest
    Assert (@((Send 'GET' '/api/menu').Data).Count -eq 0) 'Restart restored intentionally deleted menu items.'
    Write-Output 'PASS: CORS, persistence across restart, deletion, no reseeding of an emptied menu'
    Write-Output "Passed $script:checks HTTP checks. Isolated database and logs: $testRoot"
} finally {
    Stop-ServiceUnderTest
    $client.Dispose()
}
