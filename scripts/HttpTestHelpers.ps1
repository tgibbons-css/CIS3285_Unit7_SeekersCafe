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

