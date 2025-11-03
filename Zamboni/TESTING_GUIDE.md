# Testing Guide: NHL Legacy Conversion

This guide outlines the testing process for converting Zamboni from NHL 10 to NHL Legacy.

## Overview

The testing process involves:
1. Setting up a local Blaze SDK server (Zamboni)
2. Configuring RPCS3 to redirect network traffic to your local server
3. Capturing and analyzing what the NHL Legacy client sends vs what it expects to receive
4. Identifying differences from NHL 10 implementation
5. Iteratively fixing and testing until the client successfully connects

## Prerequisites

- Zamboni server running locally
- RPCS3 emulator configured with NHL Legacy
- Network packet capture tool (Wireshark recommended)
- Text editor for examining logs
- Access to server logs directory (`logs/`)

## Step 1: Initial Server Setup

1. **Configure Zamboni Server**
   - Ensure `zamboni-config.yml` exists with appropriate settings:
     ```yaml
     GameServerIp: "127.0.0.1"  # Use localhost for testing
     GameServerPort: 13337
     LogLevel: "Trace"  # Use Trace for maximum detail during testing
     ```

2. **Start the Zamboni Server**
   - Run the server and verify both services start:
     - Redirector Server on port **42100**
     - Core Server on port **13337** (or your configured port)
   - Check console output for "Zamboni server X.X started"

3. **Verify Server Logging**
   - Check that logs are being written to `logs/server-YYYY-MM-DD.log`
   - Set LogLevel to "Trace" to capture all Blaze protocol interactions

## Step 2: Configure RPCS3 Network Redirection

1. **RPCS3 Network Settings**
   - Configure RPCS3 to redirect network traffic to your local server:
     - Set DNS or use hosts file to redirect blaze server domains to `127.0.0.1`
     - Common domains to redirect:
       - `eaosp-blaze.ea.com` → `127.0.0.1`
       - `blaze.ea.com` → `127.0.0.1`
       - Or check NHL Legacy's specific endpoints

2. **Alternative: Packet Capture Setup**
   - Use Wireshark to monitor traffic between RPCS3 and your server
   - Filter: `tcp.port == 42100 || tcp.port == 13337`
   - Capture all packets for analysis

## Step 3: First Connection Test

1. **Launch NHL Legacy in RPCS3**
   - Start the game and attempt to connect to online services
   - Watch the Zamboni server console for incoming connections

2. **Observe Initial Handshake**
   - The client should connect to port **42100** (Redirector)
   - Redirector should return server instance info pointing to port **13337**
   - Client should then connect to Core server on **13337**

3. **Check for Immediate Failures**
   - If connection fails at redirector stage: Check redirector response format
   - If connection fails at core stage: Check component IDs and PreAuth response

## Step 4: Capture and Analyze Differences

### A. PreAuth/Component Differences

**What to Check:**
- Component IDs requested by NHL Legacy client
- Compare with current NHL 10 implementation in `UtilComponent.cs`:
  ```csharp
  mComponentIds = new List<ushort>
  {
      1, 4, 5, 7, 9, 10, 11, 13, 15, 21, 30722, 12,
      2049, // NHL10 Specific Component
      69   // NHL10 Specific Component
  }
  ```

**Action Items:**
- [ ] Capture the PreAuth request from NHL Legacy client
- [ ] Identify which component IDs NHL Legacy requests
- [ ] Note if component IDs 2049 or 69 are present (NHL10 specific)
- [ ] Check if NHL Legacy requests different/additional components

### B. Game Protocol Version String

**What to Check:**
- Protocol version string used by NHL Legacy
- Current implementation uses: `"NHL10_1.00"` in `ZamboniGame.cs`

**Action Items:**
- [ ] Capture game creation requests from NHL Legacy client
- [ ] Note the `mGameProtocolVersionString` value in requests
- [ ] Update hardcoded protocol version if different (e.g., "NHL14_1.00" or "NHL15_1.00")
- [ ] Check all locations where protocol version is used

### C. Component-Specific RPCs

**What to Check:**
- Custom component RPCs called by NHL Legacy
- NHL 10 specific components:
  - **Component 2049**: `OsdkSettingsComponent` 
  - **Component 69**: `DynamicMessagingComponent`

**Action Items:**
- [ ] Monitor which custom components NHL Legacy calls
- [ ] Check if NHL Legacy uses the same component IDs
- [ ] Identify any new RPC methods not present in NHL 10
- [ ] Update component bases if method signatures differ

### D. Authentication Flow

**What to Check:**
- Authentication sequence differences
- Current flow: PreAuth → Auth → PostAuth

**Action Items:**
- [ ] Trace the complete authentication flow
- [ ] Compare request/response structures
- [ ] Check if NHL Legacy requires different authentication data
- [ ] Verify session management works correctly

### E. PostAuth Response Differences

**What to Check:**
- Ticker server configuration (currently hardcoded for NHL 10)
  - Location: `UtilComponent.cs` → `PostAuthAsync()`
  - Current: `"10," + GameServerIp + ":8999,nhl-2010-ps3,10,50,50,50,50,0,0"`

**Action Items:**
- [ ] Check if NHL Legacy expects different ticker server format
- [ ] Verify port 8999 is correct for NHL Legacy
- [ ] Update game identifier if needed (e.g., "nhl-2014-ps3")

### F. Game Manager Differences

**What to Check:**
- Game creation requests and responses
- Game attributes used by NHL Legacy
- Matchmaking flow differences

**Action Items:**
- [ ] Capture game creation requests from NHL Legacy
- [ ] Compare game attributes with NHL 10 implementation
- [ ] Check if OSDK attributes differ (OSDK_gameMode, OSDK_roomId, etc.)
- [ ] Test matchmaking flow if available

### G. Port Configuration

**What to Check:**
- Ports used by NHL Legacy client
- Current configuration:
  - Redirector: 42100
  - Core Server: 13337
  - Ticker: 8999
  - Telemetry: 9946
  - QoS: 17502

**Action Items:**
- [ ] Verify NHL Legacy connects to port 42100 (redirector)
- [ ] Check if any additional ports are needed
- [ ] Confirm telemetry and ticker ports are correct

## Step 5: Systematic Testing Process

### Test Sequence

1. **Test 1: Redirector Connection**
   - [ ] Client connects to redirector (port 42100)
   - [ ] Redirector returns valid server instance info
   - [ ] Client successfully redirects to core server

2. **Test 2: PreAuth**
   - [ ] Client sends PreAuth request
   - [ ] Server responds with correct component IDs
   - [ ] No errors in console

3. **Test 3: Authentication**
   - [ ] Client completes authentication
   - [ ] Session created successfully
   - [ ] PostAuth completes

4. **Test 4: Component Requests**
   - [ ] Test each component individually
   - [ ] Verify responses match expected format
   - [ ] Check for component-specific errors

5. **Test 5: Game Functionality**
   - [ ] Create game/lobby
   - [ ] Join game
   - [ ] Matchmaking (if applicable)
   - [ ] Game session management

### Logging and Debugging

**Server Logs:**
- Review `logs/server-YYYY-MM-DD.log` after each test
- Look for ERROR or WARN messages
- Check for stack traces indicating protocol mismatches

**Console Output:**
- Watch for incoming RPC calls
- Note any "Unknown command" or "Unsupported" messages
- Monitor component registration

**Packet Capture:**
- Use Wireshark to capture raw Blaze protocol packets
- Compare request/response pairs
- Identify malformed responses

## Step 6: Common Issues and Solutions

### Issue: Client connects but immediately disconnects
**Solution:** Check PreAuth component IDs match what client expects

### Issue: "Component not found" errors
**Solution:** Add missing component to server or remove from PreAuth response

### Issue: Game creation fails
**Solution:** Check protocol version string and game attributes

### Issue: Authentication fails
**Solution:** Verify authentication flow matches NHL Legacy expectations

### Issue: Client doesn't connect to redirector
**Solution:** Check RPCS3 network configuration and DNS/hosts file redirects

## Step 7: Documentation of Changes

As you identify differences, document:

1. **Component Changes**
   - Which components are different
   - New RPC methods required
   - Modified request/response structures

2. **Protocol Changes**
   - Protocol version string
   - Port differences
   - Game-specific attributes

3. **Code Locations**
   - Files that need modification
   - Specific methods to update
   - Hardcoded values to change

4. **Testing Results**
   - What works vs what doesn't
   - Error messages encountered
   - Successful connection flows

## Step 8: Iterative Fix and Test Cycle

1. Make a change to address identified difference
2. Restart Zamboni server
3. Test connection with NHL Legacy client
4. Review logs and packet captures
5. Repeat until successful connection

## Recommended Tools

- **Wireshark**: Packet capture and analysis
- **tcpdump**: Alternative packet capture (command line)
- **Log viewer**: For examining Zamboni server logs
- **Hex editor**: For analyzing binary protocol data (if needed)
- **Blaze protocol documentation**: Reference for understanding Blaze SDK structure

## Next Steps After Successful Connection

Once basic connection works:

1. Test multiplayer functionality
2. Test all game modes
3. Test persistent data (leaderboards, stats, etc.)
4. Performance testing
5. Error handling and edge cases

## Notes

- NHL Legacy may use the same Blaze SDK version as NHL 10, requiring minimal changes
- Or it may use a different version, requiring more extensive modifications
- Component IDs and RPC methods are the most likely sources of differences
- Port numbers are usually consistent across EA games but verify
- Protocol version strings are game-specific and must match exactly
