# Quick Guide: Testing Zamboni with NHL Legacy on RPCS3

## Original EA auth server
Name:    gosredirector.ea.com
Address:  159.153.51.18
PORT: 42127

## Server Configuration

Your Zamboni server is now configured to use `127.0.0.1` for local testing. 
**Important:** Restart your Zamboni server for the config changes to take effect.

## Configure RPCS3 Network Redirection

You need to redirect EA's Blaze server domains to your local Zamboni server.

### Method 1: Using WSL2 Hosts File (Recommended)

Since you're using WSL2, you need to redirect EA domains to the WSL2 IP address, not 127.0.0.1.

**Get your WSL2 IP:** `123.1.1.123` (already discovered)

**Option A: Using RPCS3's IP/Hosts switches (Recommended - Easier)**

In RPCS3 Network Configuration:
- Set IP/Hosts switches to:
```
gosredirector.ea.com=127.0.0.1
```

**Option B: Using Windows hosts file**

1. **Open Windows hosts file as Administrator:**
   - Press `Win + R`
   - Type: `notepad C:\Windows\System32\drivers\etc\hosts`
   - Right-click Notepad and "Run as administrator"

2. **Add this entry with YOUR WSL2 IP:**
   ```
   123.1.1.123 gosredirector.ea.com
   ```

3. **Save the file**

**Verify connectivity from Windows** (run in PowerShell as Administrator):
```powershell
Test-NetConnection -ComputerName 123.1.1.123 -Port 42127
Test-NetConnection -ComputerName 123.1.1.123 -Port 13337
```
Both should show "TcpTestSucceeded : True"

### Method 2: Using RPCS3's Network Settings

Some RPCS3 builds allow DNS redirection in settings:
1. Go to RPCS3 → Configuration → Network
2. Set IP/Hosts switches to gosredirector.ea.com=127.0.0.1

## Testing Steps

1. **Restart Zamboni server** (to pick up config changes)
   ```bash
   # Stop current server (Ctrl+C), then restart
   ```

2. **Launch NHL Legacy in RPCS3**

3. **Navigate to online modes** in the game (try to connect to EA servers)

4. **Watch the Zamboni console** - You should see:
   - Connection attempts on port 42127 (Redirector)
   - Then connections on port 13337 (Core Server)
   - PreAuth requests
   - Authentication attempts

## Verify Connectivity (Important!)

Before launching RPCS3, verify Windows can reach WSL2:

**In Windows PowerShell (run as Administrator):**
```powershell
# Test redirector port
Test-NetConnection -ComputerName 123.1.1.123 -Port 42100

# Test core server port  
Test-NetConnection -ComputerName 123.1.1.123 -Port 13337
```

Both should return `TcpTestSucceeded : True`. If they fail:
- Ports might be blocked by Windows Firewall
- WSL2 networking might need configuration
- Check WSL2 IP hasn't changed: `wsl hostname -I` in Windows CMD

**Alternative verification from WSL2:**
```bash
# Check if ports are listening
netstat -tuln | grep -E '42100|13337'
```

## What to Look For

### Successful Connection Indicators:
- ✅ Client connects to redirector (port 42100)
- ✅ Redirector returns server info
- ✅ Client connects to core server (port 13337)
- ✅ PreAuth request received
- ✅ Authentication flow begins

### Common Issues:

**Client doesn't connect at all:**
- **Hosts file:** Use WSL2 IP (123.1.1.123) not 127.0.0.1
- **Firewall:** Windows Firewall may block WSL2 ports
  - Add firewall rules in PowerShell (as Admin):
    ```powershell
    New-NetFirewallRule -DisplayName "WSL2 Zamboni Redirector" -Direction Inbound -LocalPort 42100 -Protocol TCP -Action Allow
    New-NetFirewallRule -DisplayName "WSL2 Zamboni Core" -Direction Inbound -LocalPort 13337 -Protocol TCP -Action Allow
    ```
- **WSL2 IP changed:** Run `wsl hostname -I` in Windows CMD to get current IP

**Connection fails immediately:**
- Check Zamboni logs in `logs/server-YYYY-MM-DD.log`
- Look for component ID mismatches in PreAuth
- Verify LogLevel is set to "Trace" for detailed output

**Authentication fails:**
- This is expected - NHL Legacy likely uses different auth flow than NHL 10
- Check logs for specific RPC calls and component IDs

## Monitoring Logs

Watch logs in real-time:
```bash
tail -f Zamboni/logs/server-$(date +%Y-%m-%d).log
```

Or check the console output from the Zamboni server.

## Next Steps

Once you see connection attempts:
1. Note any component IDs requested (check PreAuth logs)
2. Identify protocol version strings used
3. Compare with NHL 10 implementation to find differences
4. Update Zamboni code accordingly

See `Zamboni/TESTING_GUIDE.md` for detailed testing procedures.

