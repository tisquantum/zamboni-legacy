# NHL Legacy Testing Checklist

Quick reference checklist for testing NHL Legacy compatibility.

## Pre-Testing Setup
- [ ] Zamboni server running on localhost
- [ ] Config file set with `LogLevel: "Trace"`
- [ ] RPCS3 configured to redirect to localhost
- [ ] Wireshark/tcpdump ready for packet capture
- [ ] Log directory accessible

## Initial Connection Tests

### Basic Connection
- [ ] Client connects to redirector (port 42100)
- [ ] Redirector returns server info
- [ ] Client connects to core server (port 13337)
- [ ] No immediate disconnection

### PreAuth Test
- [ ] PreAuth request received
- [ ] Component IDs in response match client expectations
- [ ] Check if components 2049 and 69 are needed for NHL Legacy
- [ ] Note any additional components requested

**Current Component IDs (NHL 10):**
```
1, 4, 5, 7, 9, 10, 11, 13, 15, 21, 30722, 12, 2049, 69
```

**NHL Legacy Component IDs:**
- [ ] Document here: ________________

### Authentication Test
- [ ] Auth request successful
- [ ] PostAuth completes
- [ ] No authentication errors

**Check PostAuth Ticker Server:**
Current: `"10," + GameServerIp + ":8999,nhl-2010-ps3,10,50,50,50,50,0,0"`
NHL Legacy format: [ ] Document here: ________________

### Component Testing

#### Util Component (1)
- [ ] Ping works
- [ ] FetchClientConfig works
- [ ] PostAuth works

#### Authentication Component (4)
- [ ] Login successful
- [ ] Session created

#### User Sessions Component (5)
- [ ] Session management works

#### Messaging Component (10)
- [ ] Messaging functions work

#### Game Manager Component (7)
- [ ] Game creation works
- [ ] Game join works
- [ ] Matchmaking works (if applicable)

**Check Game Protocol Version:**
Current: `"NHL10_1.00"`
NHL Legacy: [ ] Document here: ________________

### NHL Legacy Specific Components
- [ ] Component 2049 (OsdkSettings) - Check if needed
- [ ] Component 69 (DynamicMessaging) - Check if needed
- [ ] Any new components identified

## Key Differences to Document

### 1. Component IDs
```
NHL 10:  [list current]
NHL Legacy: [document after testing]
```

### 2. Protocol Version String
```
NHL 10:  "NHL10_1.00"
NHL Legacy: [document after testing]
```

### 3. Game Attributes
```
NHL 10 OSDK attributes documented in ZamboniGame.cs
NHL Legacy: [document differences]
```

### 4. Port Numbers
```
Redirector: 42100 - [ ] Verified
Core Server: 13337 - [ ] Verified  
Ticker: 8999 - [ ] Verified
Telemetry: 9946 - [ ] Verified
QoS: 17502 - [ ] Verified
Any additional ports: [ ] Document
```

### 5. Ticker Server Format
```
NHL 10: "10," + IP + ":8999,nhl-2010-ps3,10,50,50,50,50,0,0"
NHL Legacy: [document format after testing]
```

## Error Tracking

Document any errors encountered:

| Error/Issue | Component | Resolution |
|------------|-----------|------------|
|             |           |            |
|             |           |            |
|             |           |            |

## Code Changes Needed

Based on testing results, mark which files need updates:

- [ ] `Program.cs` - Component registration
- [ ] `Components/Blaze/UtilComponent.cs` - Component IDs, PostAuth, config
- [ ] `ZamboniGame.cs` - Protocol version, game attributes
- [ ] `Components/NHL10/OsdkSettingsComponent.cs` - May need NHL Legacy version
- [ ] `Components/NHL10/DynamicMessagingComponent.cs` - May need NHL Legacy version
- [ ] New component files needed (list): ________________

## Testing Status

- [ ] Basic connection successful
- [ ] Authentication successful  
- [ ] Component RPCs working
- [ ] Game creation working
- [ ] Matchmaking working (if applicable)
- [ ] Full game flow tested

## Notes Section

Add notes, observations, and findings here:
_____________________________________________
_____________________________________________
_____________________________________________
_____________________________________________
_____________________________________________

