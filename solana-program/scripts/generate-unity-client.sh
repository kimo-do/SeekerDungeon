#!/bin/bash
# Generate C# client code from Anchor IDL for Unity
# Requires: dotnet tool install -g Solana.Unity.Anchor.Tool

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
IDL_PATH="$PROJECT_ROOT/target/idl/chaindepth.json"
OUTPUT_PATH="$PROJECT_ROOT/../Assets/Scripts/Solana/Generated/ChainDepthClient.cs"

echo "=== Generate Unity C# Client ==="
echo "IDL: $IDL_PATH"
echo "Output: $OUTPUT_PATH"

# Check IDL exists
if [ ! -f "$IDL_PATH" ]; then
    echo "Error: IDL not found at $IDL_PATH"
    echo "Run 'anchor build' first to generate the IDL."
    exit 1
fi

# Ensure output directory exists
mkdir -p "$(dirname "$OUTPUT_PATH")"

# Generate C# client
echo "Generating..."
dotnet anchorgen -i "$IDL_PATH" -o "$OUTPUT_PATH"

echo "Success! Generated: $OUTPUT_PATH"
