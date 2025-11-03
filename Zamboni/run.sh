#!/bin/bash

# Zamboni Server Run Script

echo "=== Zamboni Server Launcher ==="
echo ""

# Check if .NET is installed
if ! command -v dotnet &> /dev/null
then
    echo "❌ .NET SDK not found!"
    echo "Please install .NET 9.0 SDK first:"
    echo "  sudo snap install dotnet-sdk --classic --channel=9.0"
    echo "  or"
    echo "  sudo apt-get install -y dotnet-sdk-9.0"
    exit 1
fi

echo "✓ .NET version: $(dotnet --version)"
echo ""

# Change to script directory
cd "$(dirname "$0")"

echo "📦 Restoring dependencies..."
dotnet restore

if [ $? -ne 0 ]; then
    echo "❌ Failed to restore dependencies"
    exit 1
fi

echo ""
echo "🔨 Building project..."
dotnet build

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo ""
echo "🚀 Starting Zamboni server..."
echo "Press Ctrl+C to stop"
echo ""

dotnet run

