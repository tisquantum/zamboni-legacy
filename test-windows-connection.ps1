# PowerShell script to test connection to WSL2 server and set up port forwarding
# Run this from Windows PowerShell (not WSL)

$WSL_IP = "123.1.1.123"  # Your WSL2 IP
$PORT = 13337

Write-Host "=== Testing WSL2 Connection to Port $PORT ===" -ForegroundColor Cyan

# Method 1: Test direct connection to WSL IP
Write-Host "`n1. Testing direct connection to WSL IP ($WSL_IP:$PORT)..." -ForegroundColor Yellow
try {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $connect = $tcpClient.BeginConnect($WSL_IP, $PORT, $null, $null)
    $wait = $connect.AsyncWaitHandle.WaitOne(2000, $false)
    
    if ($wait) {
        $tcpClient.EndConnect($connect)
        Write-Host "   ✓ SUCCESS: Can connect directly to WSL IP" -ForegroundColor Green
        $tcpClient.Close()
    } else {
        Write-Host "   ✗ FAILED: Connection timeout to WSL IP" -ForegroundColor Red
    }
} catch {
    Write-Host "   ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

# Method 2: Test localhost port forwarding
Write-Host "`n2. Testing localhost:$PORT (requires port forwarding)..." -ForegroundColor Yellow
try {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $connect = $tcpClient.BeginConnect("127.0.0.1", $PORT, $null, $null)
    $wait = $connect.AsyncWaitHandle.WaitOne(2000, $false)
    
    if ($wait) {
        $tcpClient.EndConnect($connect)
        Write-Host "   ✓ SUCCESS: Port forwarding is working!" -ForegroundColor Green
        $tcpClient.Close()
    } else {
        Write-Host "   ✗ FAILED: No port forwarding configured" -ForegroundColor Red
        Write-Host "   → Setting up port forwarding..." -ForegroundColor Yellow
        
        # Check if forwarding already exists
        $existing = netsh interface portproxy show v4tov4 | Select-String "listenport=$PORT"
        if ($existing) {
            Write-Host "   → Port forwarding rule already exists, removing old one..." -ForegroundColor Yellow
            netsh interface portproxy delete v4tov4 listenport=$PORT > $null 2>&1
        }
        
        # Create new port forwarding rule
        netsh interface portproxy add v4tov4 listenport=$PORT listenaddress=0.0.0.0 connectport=$PORT connectaddress=$WSL_IP
        if ($LASTEXITCODE -eq 0) {
            Write-Host "   ✓ Port forwarding configured!" -ForegroundColor Green
            Write-Host "   → Testing again..." -ForegroundColor Yellow
            
            Start-Sleep -Milliseconds 500
            $tcpClient2 = New-Object System.Net.Sockets.TcpClient
            $connect2 = $tcpClient2.BeginConnect("127.0.0.1", $PORT, $null, $null)
            $wait2 = $connect2.AsyncWaitHandle.WaitOne(2000, $false)
            
            if ($wait2) {
                $tcpClient2.EndConnect($connect2)
                Write-Host "   ✓ SUCCESS: Connection works after port forwarding setup!" -ForegroundColor Green
                $tcpClient2.Close()
            } else {
                Write-Host "   ✗ Still failing - may need to check Windows Firewall" -ForegroundColor Red
            }
        } else {
            Write-Host "   ✗ Failed to set up port forwarding. Run PowerShell as Administrator." -ForegroundColor Red
        }
    }
} catch {
    Write-Host "   ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

# Show current port forwarding rules
Write-Host "`n3. Current port forwarding rules for port $PORT:" -ForegroundColor Cyan
netsh interface portproxy show v4tov4 | Select-String "listenport=$PORT" | ForEach-Object {
    Write-Host "   $_" -ForegroundColor Gray
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan
Write-Host "`nTo remove port forwarding later, run as Administrator:" -ForegroundColor Yellow
Write-Host "   netsh interface portproxy delete v4tov4 listenport=$PORT" -ForegroundColor Gray

