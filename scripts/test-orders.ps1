param([int]$MenuPort = 5291, [int]$OrderPort = 5292)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$workspace = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $workspace ('.local/order-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(8)
$menuUrl = "http://localhost:$MenuPort"
$orderUrl = "http://localhost:$OrderPort"
$processes = @{}
$script:starts = 0
$script:checks = 0
$faultServer = $null

function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

function Send([string]$Method, [string]$Url, $Body = $null, [int]$Expected = 200, $Headers = @{}) {
    $methodObject = New-Object System.Net.Http.HttpMethod -ArgumentList $Method
    $request = New-Object System.Net.Http.HttpRequestMessage -ArgumentList $methodObject, $Url
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { ConvertTo-Json -InputObject $Body -Depth 8 -Compress }
            $request.Content = New-Object System.Net.Http.StringContent -ArgumentList $json, ([Text.Encoding]::UTF8), 'application/json'
        }
        foreach ($key in $Headers.Keys) { $request.Headers.Add($key, [string]$Headers[$key]) }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Assert ([int]$response.StatusCode -eq $Expected) "$Method $Url expected $Expected, got $([int]$response.StatusCode): $text"
            $script:checks++
            $data = if ($text) { ConvertFrom-Json -InputObject $text } else { $null }
            if ($text -eq '[]') { $data = @() }
            return @{ Data = $data; Headers = $response.Headers }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

function Start-App([string]$Name, [int]$Port, [string]$Extra) {
    $serviceRoot = Join-Path $workspace "src/$Name"
    $dll = Join-Path $serviceRoot "bin/Debug/net10.0/$Name.dll"
    Assert (Test-Path -LiteralPath $dll) 'Build the solution in Debug configuration first.'
    $listener = New-Object Net.Sockets.TcpListener -ArgumentList ([Net.IPAddress]::Loopback), $Port
    try { $listener.Start() } finally { $listener.Stop() }
    $script:starts++
    $arguments = '"{0}" --urls http://localhost:{1} --contentRoot "{2}" {3}' -f $dll, $Port, $serviceRoot, $Extra
    $processes[$Name] = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $testRoot "$script:starts-$Name.stdout.log") `
        -RedirectStandardError (Join-Path $testRoot "$script:starts-$Name.stderr.log")
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($processes[$Name].HasExited) { throw "$Name exited. Inspect $testRoot" }
        try {
            $response = $client.GetAsync("http://localhost:$Port/").GetAwaiter().GetResult()
            $ready = $response.IsSuccessStatusCode
            $response.Dispose()
            if ($ready) { return }
        } catch { }
        Start-Sleep -Milliseconds 200
    }
    throw "$Name startup timed out."
}

function Stop-App([string]$Name) {
    if ($processes.ContainsKey($Name) -and -not $processes[$Name].HasExited) {
        Stop-Process -Id $processes[$Name].Id
        $processes[$Name].WaitForExit()
    }
}

# Test-only HTTP server: deterministic upstream faults without changing application code.
if (-not ('MenuFaultServer' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
public sealed class MenuFaultServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly object gate = new object();
    private readonly List<TcpClient> clients = new List<TcpClient>();
    private bool stopped;
    public MenuFaultServer(int port)
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Task.Run((Action)Run);
    }
    private void Run()
    {
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); } catch { return; }
            lock (gate)
            {
                if (stopped) { client.Dispose(); return; }
                clients.Add(client);
            }
            Task.Run(() => Respond(client));
        }
    }
    private static void Respond(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 3000;
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var line = reader.ReadLine() ?? "";
                while (!String.IsNullOrEmpty(reader.ReadLine())) { }
                string body = "{bad json";
                string status = "200 OK";
                if (line.Contains("/900 ")) { status = "500 Internal Server Error"; body = "{}"; }
                if (line.Contains("/902 ")) body = "{\"menuItemId\":902}";
                if (line.Contains("/903 ")) body = "{\"menuItemId\":999,\"name\":\"Wrong item\",\"price\":1,\"isAvailable\":true}";
                if (line.Contains("/904 ")) Thread.Sleep(3500);
                var bytes = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                stream.Write(headers, 0, headers.Length);
                stream.Write(bytes, 0, bytes.Length);
            }
            catch { /* Client may disconnect after the intentional timeout. */ }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            stopped = true;
            listener.Stop();
            // Close accepted/idle sockets too, so a pooled connection cannot keep
            // serving faults after the real MenuService reclaims this port.
            foreach (var client in clients) client.Dispose();
        }
    }
}
'@
}

$menuConfig = '--ConnectionStrings:MenuDatabase "Data Source={0}"' -f (Join-Path $testRoot 'menu.db')
$orderConfig = '--ConnectionStrings:OrderDatabase "Data Source={0}" --MenuService:BaseUrl {1}/ --MenuService:TimeoutSeconds 2' -f (Join-Path $testRoot 'orders.db'), $menuUrl
try {
    Start-App 'MenuService' $MenuPort $menuConfig
    Start-App 'OrderService' $OrderPort $orderConfig
    Assert ((Send 'GET' "$orderUrl/api/orders").Data.Count -eq 0) 'Expected a fresh order database.'
    $menu = (Send 'GET' "$menuUrl/api/menu").Data
    $latte = @($menu | Where-Object name -eq 'Latte')[0]
    $muffin = @($menu | Where-Object name -eq 'Blueberry Muffin')[0]
    $body = @{ customerName = ' Classroom Customer '; items = @(
        @{ menuItemId = $latte.menuItemId; quantity = 2; unitPrice = 0.01; itemName = 'Forged' },
        @{ menuItemId = $muffin.menuItemId; quantity = 1 }
    ); totalAmount = 0.01; status = 'Paid'; orderId = 123456; orderDate = '2000-01-01' }
    $created = Send 'POST' "$orderUrl/api/orders" $body 201
    $order = $created.Data
    $id = $order.orderId
    Assert ($order.status -eq 'Pending' -and $order.totalAmount -eq 12) 'Client changed server status or total.'
    Assert ($order.customerName -eq 'Classroom Customer' -and $id -ne 123456) 'Name normalization or ID generation failed.'
    Assert ($created.Headers.Location.ToString() -eq "/api/orders/$id") 'Location header is incorrect.'
    Assert ($order.orderItems.Count -eq 2) 'Order lines missing.'
    Assert (@($order.orderItems | Where-Object menuItemId -eq $latte.menuItemId)[0].itemName -eq 'Latte') 'Client forged the item name.'
    Assert ($order.orderDate.ToString() -notlike '2000*') 'Client forged order date.'
    $retrieved = (Send 'GET' "$orderUrl/api/orders/$id").Data
    Assert ($retrieved.totalAmount -eq 12) 'Stored order total changed.'
    Write-Output 'PASS: real HTTP menu validation, server pricing, snapshots, Pending status'

    $menuChange = @{ name = 'Changed Latte'; description = 'Updated'; category = 'Coffee'; price = 9.99; isAvailable = $false }
    $null = Send 'PUT' "$menuUrl/api/menu/$($latte.menuItemId)" $menuChange
    $retrieved = (Send 'GET' "$orderUrl/api/orders/$id").Data
    Assert ($retrieved.totalAmount -eq 12) 'Menu edit rewrote order total.'
    $snapshot = @($retrieved.orderItems | Where-Object menuItemId -eq $latte.menuItemId)[0]
    Assert ($snapshot.itemName -eq 'Latte' -and $snapshot.unitPrice -eq 4.50) 'Menu edit rewrote order snapshots.'
    $null = Send 'POST' "$orderUrl/api/orders" $body 400
    $null = Send 'DELETE' "$menuUrl/api/menu/$($muffin.menuItemId)" $null 204
    $missing = @{ customerName = 'Missing'; items = @(@{ menuItemId = $muffin.menuItemId; quantity = 1 }) }
    $null = Send 'POST' "$orderUrl/api/orders" $missing 400
    Assert ((Send 'GET' "$orderUrl/api/orders/$id").Data.orderItems.Count -eq 2) 'Menu deletion damaged order history.'

    foreach ($invalid in @(
        @{}, @{ customerName = ' '; items = @(@{ menuItemId = 1; quantity = 1 }) },
        @{ customerName = ('x' * 101); items = @(@{ menuItemId = 1; quantity = 1 }) },
        @{ customerName = 'Test'; items = @() }, @{ customerName = 'Test'; items = @($null) },
        @{ customerName = 'Test'; items = @(@{ menuItemId = 0; quantity = 1 }) },
        @{ customerName = 'Test'; items = @(@{ menuItemId = 1; quantity = 0 }) },
        @{ customerName = 'Test'; items = @(@{ menuItemId = 1; quantity = 21 }) },
        @{ customerName = 'Test'; items = @(@{ menuItemId = 1; quantity = 1 }, @{ menuItemId = 1; quantity = 1 }) },
        @{ customerName = 'Test'; items = @(1..51 | ForEach-Object { @{ menuItemId = $_; quantity = 1 } }) }
    )) { $null = Send 'POST' "$orderUrl/api/orders" $invalid 400 }
    $null = Send 'POST' "$orderUrl/api/orders" '{bad json' 400
    $null = Send 'POST' "$orderUrl/api/orders" 'null' 400
    $null = Send 'GET' "$orderUrl/api/orders/2147483647" $null 404
    $null = Send 'PUT' "$orderUrl/api/orders/2147483647/status" @{ status = 'Cancelled' } 404
    foreach ($status in @('Paid', 'Preparing', 'Ready', 'Completed')) {
        $null = Send 'PUT' "$orderUrl/api/orders/$id/status" @{ status = $status } 409
    }
    foreach ($status in @('Bogus', 'cancelled', '1', '')) {
        $null = Send 'PUT' "$orderUrl/api/orders/$id/status" @{ status = $status } 400
    }
    $null = Send 'PUT' "$orderUrl/api/orders/$id/status" @{} 400
    $null = Send 'PUT' "$orderUrl/api/orders/$id/status" @{ status = 'Pending' }
    $cancelled = Send 'PUT' "$orderUrl/api/orders/$id/status" @{ status = 'Cancelled' }
    Assert ($cancelled.Data.status -eq 'Cancelled') 'Cancellation failed.'
    $null = Send 'PUT' "$orderUrl/api/orders/$id/status" @{ status = 'Cancelled' }
    $null = Send 'PUT' "$orderUrl/api/orders/$id/status" @{ status = 'Pending' } 409
    Assert ((Send 'GET' "$orderUrl/api/orders").Data.Count -eq 1) 'Rejected requests created partial orders.'
    Write-Output 'PASS: validation, unavailable/missing products, immutable history, status transitions'

    $allowed = Send 'GET' "$orderUrl/api/orders" $null 200 @{ Origin = 'http://localhost:5100' }
    Assert ($allowed.Headers.GetValues('Access-Control-Allow-Origin') -contains 'http://localhost:5100') 'Expected frontend CORS origin.'
    $denied = Send 'GET' "$orderUrl/api/orders" $null 200 @{ Origin = 'https://example.org' }
    Assert (-not $denied.Headers.Contains('Access-Control-Allow-Origin')) 'Unexpected CORS origin.'

    Stop-App 'MenuService'
    $valid = @{ customerName = 'Outage'; items = @(@{ menuItemId = 3; quantity = 1 }) }
    $failure = Send 'POST' "$orderUrl/api/orders" $valid 503
    Assert ($failure.Data.detail -like '*No order was created*') 'Outage response was unclear.'
    Assert ((Send 'GET' "$orderUrl/api/orders/$id").Data.status -eq 'Cancelled') 'Order reads depend on MenuService.'
    $null = Send 'POST' "$orderUrl/api/orders" @{} 400
    Stop-App 'OrderService'
    Start-App 'OrderService' $OrderPort $orderConfig
    Assert ((Send 'GET' "$orderUrl/api/orders/$id").Data.status -eq 'Cancelled') 'Order restart lost cancellation.'
    Assert ((Send 'GET' "$orderUrl/api/orders/$id").Data.totalAmount -eq 12) 'Order restart lost money values.'

    $faultServer = New-Object MenuFaultServer -ArgumentList $MenuPort
    foreach ($faultId in @(900, 901, 902, 903, 904)) {
        $fault = @{ customerName = 'Fault'; items = @(@{ menuItemId = $faultId; quantity = 1 }) }
        $failure = Send 'POST' "$orderUrl/api/orders" $fault 503
        if ($faultId -eq 904) { Assert ($failure.Data.detail -like '*timed out*') 'Expected a clear timeout response.' }
    }
    Assert ((Send 'GET' "$orderUrl/api/orders").Data.Count -eq 1) 'Dependency failures persisted an order.'
    $faultServer.Dispose(); $faultServer = $null
    Start-App 'MenuService' $MenuPort $menuConfig
    $recovered = Send 'POST' "$orderUrl/api/orders" $valid 201
    Assert ($recovered.Data.status -eq 'Pending' -and $recovered.Data.totalAmount -eq 2.50) 'No recovery after menu restart.'
    $listed = (Send 'GET' "$orderUrl/api/orders").Data
    Assert ($listed.Count -eq 2 -and $listed[0].orderId -eq $recovered.Data.orderId) 'Orders not listed newest first.'
    Write-Output 'PASS: CORS, persistence, outage, upstream 500, malformed contract, timeout, recovery'
    Write-Output "Passed $script:checks HTTP checks. Isolated databases and logs: $testRoot"
} catch {
    # Allow the asynchronous console logger to flush before stopping failed test apps.
    Start-Sleep -Milliseconds 500
    throw
} finally {
    if ($null -ne $faultServer) { $faultServer.Dispose() }
    Stop-App 'OrderService'
    Stop-App 'MenuService'
    $client.Dispose()
}
