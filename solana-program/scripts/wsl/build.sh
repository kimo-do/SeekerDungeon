#!/bin/bash
# Build the ChainDepth Anchor program
# Run: wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/build.sh

set -e

export PATH="$HOME/.local/share/solana/install/active_release/bin:$HOME/.cargo/bin:$PATH"
source "$HOME/.cargo/env" 2>/dev/null || true

cd /mnt/e/Github2/SeekerDungeon/solana-program

echo "=== ChainDepth Build ==="
echo "Rust: $(rustc --version)"
echo "Anchor: $(anchor --version)"
echo "Solana: $(solana --version)"
echo ""

# Remove Cargo.lock to avoid version conflicts between Windows/WSL
rm -f Cargo.lock

echo "Building..."
anchor build

echo ""
echo "=== Build Complete ==="
echo "Program: target/deploy/chaindepth.so"
echo "IDL: target/idl/chaindepth.json"
