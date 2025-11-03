# How to Run Zamboni Server

## Prerequisites

1. **Install .NET 9.0 SDK**
   - Check if installed: `dotnet --version`
   - If not installed, download from: https://dotnet.microsoft.com/download/dotnet/9.0
   - The project requires .NET SDK 9.0.0 or later

2. **Install Dependencies**
   - The project has dependencies on BlazeSDK projects in the parent directory
   - Make sure the BlazeSDK project is built first

## Running in Cursor Terminal

### Step 1: Open Terminal in Cursor
- Press `` Ctrl+` `` (backtick) or go to `Terminal` → `New Terminal`
- Make sure you're in the Zamboni project directory

### Step 2: Restore Dependencies
```bash
cd /home/brett/dev/zamboni-legacy/Zamboni
dotnet restore
```

### Step 3: Build the Project
```bash
dotnet build
```

If there are any build errors related to missing BlazeSDK references, you may need to build the BlazeSDK projects first:
```bash
cd /home/brett/dev/zamboni-legacy/BlazeSDK
dotnet build
cd ../Zamboni
```

### Step 4: Run the Server
```bash
dotnet run
```

Or build and run separately:
```bash
dotnet build -c Release
dotnet bin/Release/net9.0/Zamboni.dll
```

## Configuration

Before running, the server will create a `zamboni-config.yml` file if it doesn't exist. You can edit this file to configure:

```yaml
GameServerIp: "127.0.0.1"  # Use localhost for local testing
GameServerPort: 13337
LogLevel: "Trace"  # Use Trace for detailed logging during testing
DatabaseConnectionString: "Host=localhost;Port=5432;Username=postgres;Password=password;Database=zamboni"
```

## Expected Output

When the server starts successfully, you should see:
```
[LOG] Zamboni server 1.3 started
[LOG] Redirector server listening on port 42100
[LOG] Core server listening on port 13337
```

## Stopping the Server

Press `Ctrl+C` in the terminal to stop the server gracefully.

## Troubleshooting

### Error: "Could not find project reference"
- Make sure the BlazeSDK projects are in the expected location: `../BlazeSDK/`
- Build BlazeSDK projects first: `cd ../BlazeSDK && dotnet build`

### Error: "Port already in use"
- Another instance of Zamboni might be running
- Check: `lsof -i :42100` or `lsof -i :13337`
- Kill the process or change ports in config

### Error: ".NET SDK not found"
- Install .NET 9.0 SDK from https://dotnet.microsoft.com/download
- Verify with: `dotnet --version`

## Running in Background (Optional)

To run the server in the background:
```bash
nohup dotnet run > server.log 2>&1 &
```

To check if it's running:
```bash
ps aux | grep Zamboni
```

To stop:
```bash
pkill -f Zamboni
```
