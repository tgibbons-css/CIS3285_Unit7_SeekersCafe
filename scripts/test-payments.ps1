param([int]$MenuPort = 5391, [int]$OrderPort = 5392, [int]$PaymentPort = 5393)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$workspace = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $workspace ('.local/payment-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(8)
$processes = @{}
$script:starts = 0
$script:checks = 0
. (Join-Path $PSScriptRoot 'HttpTestHelpers.ps1')
if (-not ('PaymentFaultServer' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'PaymentFaultServer.cs') }
$faultServer = $null
$menuUrl = "http://localhost:$MenuPort"
$orderUrl = "http://localhost:$OrderPort"
$paymentUrl = "http://localhost:$PaymentPort"
$menuConfig = '--ConnectionStrings:MenuDatabase "Data Source={0}"' -f (Join-Path $testRoot 'menu.db')
$orderConfig = '--ConnectionStrings:OrderDatabase "Data Source={0}" --MenuService:BaseUrl {1}/ --PaymentService:BaseUrl {2}/ --PaymentService:TimeoutSeconds 1' -f (Join-Path $testRoot 'orders.db'), $menuUrl, $paymentUrl
$paymentConfig = '--ConnectionStrings:PaymentDatabase "Data Source={0}"' -f (Join-Path $testRoot 'payments.db')

try {
    # PaymentService is tested independently before the other services start.
    Start-App 'PaymentService' $PaymentPort $paymentConfig
    $request = @{ orderId = 10000; requestId = 'independent-demo'; amount = 4.25; paymentMethod = 'Credit Card' }
    $created = Send 'POST' "$paymentUrl/api/payments" $request 201
    $paymentId = $created.Data.paymentId
    Assert ($created.Data.status -eq 'Succeeded' -and $created.Data.amount -eq 4.25) 'Independent simulation failed.'
    Assert ($created.Headers.Location.ToString() -eq "/api/payments/$paymentId") 'Incorrect payment Location.'
    Assert ((Send 'GET' "$paymentUrl/api/payments/$paymentId").Data.orderId -eq 10000) 'Payment lookup failed.'
    Assert ((Send 'GET' "$paymentUrl/api/payments/order/10000").Data.paymentId -eq $paymentId) 'Payment-by-order lookup failed.'
    Assert ((Send 'POST' "$paymentUrl/api/payments" $request).Data.paymentId -eq $paymentId) 'Identical replay created a payment.'
    foreach ($field in @('amount', 'paymentMethod', 'requestId', 'orderId')) {
        $conflict = $request.Clone()
        switch ($field) {
            'amount' { $conflict.amount = 4.26 }
            'paymentMethod' { $conflict.paymentMethod = 'Dining Dollars' }
            'requestId' { $conflict.requestId = 'new-key-same-order' }
            'orderId' { $conflict.orderId = 10001 }
        }
        $null = Send 'POST' "$paymentUrl/api/payments" $conflict 409
    }
    foreach ($field in @('orderId', 'requestId', 'amount', 'paymentMethod')) {
        $invalid = $request.Clone(); $invalid.Remove($field)
        $null = Send 'POST' "$paymentUrl/api/payments" $invalid 400
    }
    foreach ($amount in @(-1, 1000000.01, 0.001)) {
        $invalid = $request.Clone(); $invalid.amount = $amount
        $null = Send 'POST' "$paymentUrl/api/payments" $invalid 400
    }
    foreach ($key in @('', ' ', ('x' * 101), ' padded ')) {
        $invalid = $request.Clone(); $invalid.requestId = $key
        $null = Send 'POST' "$paymentUrl/api/payments" $invalid 400
    }
    $invalid = $request.Clone(); $invalid.paymentMethod = 'Real Card'
    $null = Send 'POST' "$paymentUrl/api/payments" $invalid 400
    $null = Send 'POST' "$paymentUrl/api/payments" '{bad json' 400
    $null = Send 'POST' "$paymentUrl/api/payments" 'null' 400
    $null = Send 'GET' "$paymentUrl/api/payments/2147483647" $null 404
    $null = Send 'GET' "$paymentUrl/api/payments/order/2147483647" $null 404

    # Issue identical requests concurrently, rather than testing only sequential replay.
    $parallelBody = @{ orderId = 10002; requestId = 'concurrent-demo'; amount = 0.10; paymentMethod = 'Dining Dollars' } | ConvertTo-Json -Compress
    $pending = @()
    for ($index = 0; $index -lt 8; $index++) {
        $content = New-Object System.Net.Http.StringContent -ArgumentList $parallelBody, ([Text.Encoding]::UTF8), 'application/json'
        $pending += @{ Task = $client.PostAsync("$paymentUrl/api/payments", $content); Content = $content }
    }
    $ids = @(); $newCount = 0
    foreach ($entry in $pending) {
        $response = $entry.Task.GetAwaiter().GetResult()
        try {
            Assert ([int]$response.StatusCode -in @(200, 201)) 'Concurrent replay failed.'
            if ([int]$response.StatusCode -eq 201) { $newCount++ }
            $data = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            $ids += $data.paymentId; $script:checks++
        } finally { $response.Dispose(); $entry.Content.Dispose() }
    }
    Assert ($newCount -eq 1 -and @($ids | Select-Object -Unique).Count -eq 1) 'Concurrent requests produced duplicate records.'
    Stop-App 'PaymentService'
    Start-App 'PaymentService' $PaymentPort $paymentConfig
    Assert ((Send 'POST' "$paymentUrl/api/payments" $request).Data.paymentId -eq $paymentId) 'Restart lost deduplication.'
    Write-Output 'PASS: independent payments, input validation, unique constraints, concurrent replay, persistence'

    Start-App 'MenuService' $MenuPort $menuConfig
    Start-App 'OrderService' $OrderPort $orderConfig
    $checkout = @{ customerName = 'Payment Demo'; paymentMethod = 'Dining Dollars'; items = @(@{ menuItemId = 1; quantity = 2 }) }
    $success = (Send 'POST' "$orderUrl/api/orders" $checkout 201).Data
    $paidId = $success.order.orderId
    Assert ($success.order.status -eq 'Paid' -and $success.order.totalAmount -eq 9) 'Confirmed payment did not mark order Paid.'
    Assert ($success.payment.paymentMethod -eq 'Dining Dollars') 'Wrong simulated payment method.'
    Assert ((Send 'POST' "$orderUrl/api/orders/$paidId/payment/reconcile").Data.payment.paymentId -eq $success.payment.paymentId) 'Reconciliation changed payment.'
    $null = Send 'PUT' "$orderUrl/api/orders/$paidId/status" @{ status = 'Cancelled' } 409
    foreach ($status in @('Preparing', 'Ready', 'Completed')) {
        $null = Send 'PUT' "$orderUrl/api/orders/$paidId/status" @{ status = $status }
    }
    Assert ((Send 'POST' "$orderUrl/api/orders/$paidId/payment/reconcile").Data.order.status -eq 'Completed') 'Reconcile regressed completed order.'
    $invalid = $checkout.Clone(); $invalid.Remove('paymentMethod')
    $null = Send 'POST' "$orderUrl/api/orders" $invalid 400
    $invalid.paymentMethod = 'Cash'
    $null = Send 'POST' "$orderUrl/api/orders" $invalid 400

    Stop-App 'PaymentService'
    Start-App 'PaymentService' $PaymentPort ($paymentConfig + ' --Simulation:DeclinePayments true')
    $checkout.paymentMethod = 'Credit Card'
    $declined = (Send 'POST' "$orderUrl/api/orders" $checkout 402).Data
    $declinedId = $declined.orderId
    Assert ($declined.paymentStatus -eq 'Declined' -and $declined.order.status -eq 'Pending') 'Decline was not explicit.'
    $null = Send 'PUT' "$orderUrl/api/orders/$declinedId/status" @{ status = 'Paid' } 409
    $null = Send 'PUT' "$orderUrl/api/orders/$declinedId/status" @{ status = 'Preparing' } 409
    Assert ((Send 'PUT' "$orderUrl/api/orders/$declinedId/status" @{ status = 'Cancelled' }).Data.status -eq 'Cancelled') 'Confirmed decline could not be cancelled.'
    Assert ((Send 'POST' "$orderUrl/api/orders/$declinedId/payment/reconcile").Data.order.status -eq 'Cancelled') 'Decline reconciliation regressed cancellation.'
    Assert ((Send 'GET' "$paymentUrl/api/payments/$($success.payment.paymentId)").Data.status -eq 'Succeeded') 'Config change rewrote existing payment.'
    Write-Output 'PASS: both payment methods, confirmed decline, guarded status transitions, reconciliation'

    Stop-App 'PaymentService'
    $unknown = (Send 'POST' "$orderUrl/api/orders" $checkout 503).Data
    $unknownId = $unknown.orderId
    Assert ($unknown.paymentStatus -eq 'Unknown' -and $unknown.order.status -eq 'Pending') 'Outage lost Pending order or hid uncertainty.'
    Assert ((Send 'GET' "$orderUrl/api/orders/$unknownId").Data.status -eq 'Pending') 'Pending order was not persisted before payment.'
    $null = Send 'PUT' "$orderUrl/api/orders/$unknownId/status" @{ status = 'Paid' } 409
    $null = Send 'PUT' "$orderUrl/api/orders/$unknownId/status" @{ status = 'Preparing' } 409
    $null = Send 'PUT' "$orderUrl/api/orders/$unknownId/status" @{ status = 'Cancelled' } 503
    Start-App 'PaymentService' $PaymentPort $paymentConfig
    $null = Send 'POST' "$orderUrl/api/orders/$unknownId/payment/reconcile" $null 409
    $null = Send 'PUT' "$orderUrl/api/orders/$unknownId/status" @{ status = 'Cancelled' } 409
    $null = Send 'GET' "$paymentUrl/api/payments/order/$unknownId" $null 404
    $declinedPayment = (Send 'GET' "$paymentUrl/api/payments/order/$declinedId").Data
    $declineReplay = @{ orderId = $declinedId; requestId = $declinedPayment.requestId; amount = $declinedPayment.amount; paymentMethod = $declinedPayment.paymentMethod }
    Assert ((Send 'POST' "$paymentUrl/api/payments" $declineReplay).Data.status -eq 'Declined') 'Configuration change rewrote a saved decline.'
    Write-Output 'PASS: payment outage preserves Pending, missing record does not initiate payment'

    Stop-App 'PaymentService'
    Start-App 'PaymentService' $PaymentPort ($paymentConfig + ' --Simulation:ResponseDelayMilliseconds 2500')
    $late = (Send 'POST' "$orderUrl/api/orders" $checkout 503).Data
    $lateId = $late.orderId
    Assert ($late.paymentStatus -eq 'Unknown' -and $late.order.status -eq 'Pending') 'Late response incorrectly claimed success/decline.'
    $savedPayment = (Send 'GET' "$paymentUrl/api/payments/order/$lateId").Data
    Assert ($savedPayment.status -eq 'Succeeded') 'The payment did not commit before response timeout.'
    $null = Send 'PUT' "$orderUrl/api/orders/$lateId/status" @{ status = 'Cancelled' } 409
    Stop-App 'OrderService'
    Start-App 'OrderService' $OrderPort $orderConfig
    $reconciled = (Send 'POST' "$orderUrl/api/orders/$lateId/payment/reconcile").Data
    Assert ($reconciled.order.status -eq 'Paid' -and $reconciled.payment.paymentId -eq $savedPayment.paymentId) 'Restart reconciliation failed.'
    $replay = @{ orderId = $lateId; requestId = $savedPayment.requestId; amount = $savedPayment.amount; paymentMethod = $savedPayment.paymentMethod }
    Assert ((Send 'POST' "$paymentUrl/api/payments" $replay).Data.paymentId -eq $savedPayment.paymentId) 'Replay duplicated late payment.'
    Assert ((Send 'POST' "$orderUrl/api/orders/$lateId/payment/reconcile").Data.payment.paymentId -eq $savedPayment.paymentId) 'Repeated reconciliation duplicated payment.'
    $null = Send 'POST' "$orderUrl/api/orders/2147483647/payment/reconcile" $null 404
    Write-Output 'PASS: committed payment with lost/late response, restart reconciliation, no second payment'

    Stop-App 'PaymentService'
    foreach ($case in @(
        @{ status = 500; body = '{}' }, @{ status = 200; body = '{bad json' },
        @{ status = 200; body = '{}' },
        @{ status = 200; body = '{"paymentId":1,"orderId":1,"requestId":"wrong","amount":0,"paymentMethod":"Credit Card","status":"Succeeded"}' }
    )) {
        $faultServer = New-Object PaymentFaultServer -ArgumentList $PaymentPort, $case.body, $case.status
        $fault = (Send 'POST' "$orderUrl/api/orders" $checkout 503).Data
        Assert ($fault.paymentStatus -eq 'Unknown' -and $fault.order.status -eq 'Pending') 'Invalid dependency response falsely confirmed payment.'
        $null = Send 'PUT' "$orderUrl/api/orders/$($fault.orderId)/status" @{ status = 'Preparing' } 409
        $faultServer.Dispose(); $faultServer = $null
    }
    Write-Output 'PASS: upstream 500, malformed/missing contract fields, inconsistent payment data'
    Write-Output "Passed $script:checks HTTP checks. Isolated databases and logs: $testRoot"
} catch {
    Start-Sleep -Milliseconds 500
    throw
} finally {
    if ($null -ne $faultServer) { $faultServer.Dispose() }
    Stop-App 'OrderService'
    Stop-App 'MenuService'
    Stop-App 'PaymentService'
    $client.Dispose()
}
