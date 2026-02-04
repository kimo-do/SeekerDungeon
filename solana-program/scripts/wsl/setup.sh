#!/bin/bash
# One-time setup of Rust, Solana CLI, Anchor in WSL Ubuntu
# Run: wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/setup.sh

set -e
echo "=== ChainDepth WSL Development Setup ==="

# Install Rust
if ! command -v rustc &> /dev/null; then
    echo "Installing Rust..."
    curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y
    source "$HOME/.cargo/env"
else
    echo "Rust already installed: $(rustc --version)"
fi

source "$HOME/.cargo/env"

# Install Solana CLI
if ! command -v solana &> /dev/null; then
    echo "Installing Solana CLI..."
    sh -c "$(curl -sSfL https://release.anza.xyz/stable/install)"
else
    echo "Solana CLI already installed: $(solana --version)"
fi

export PATH="$HOME/.local/share/solana/install/active_release/bin:$PATH"

# Install Anchor
if ! command -v anchor &> /dev/null; then
    echo "Installing Anchor..."
    cargo install --git https://github.com/coral-xyz/anchor avm --force
    avm install latest
    avm use latest
else
    echo "Anchor already installed: $(anchor --version)"
fi

# Install Node.js if needed
if ! command -v node &> /dev/null; then
    echo "Installing Node.js..."
    curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
    sudo apt-get install -y nodejs
else
    echo "Node.js already installed: $(node --version)"
fi

# Add to profile for persistence
if ! grep -q "solana/install" ~/.profile 2>/dev/null; then
    echo 'export PATH="$HOME/.local/share/solana/install/active_release/bin:$HOME/.cargo/bin:$PATH"' >> ~/.profile
    echo "Added PATH to ~/.profile"
fi

echo ""
echo "=== Setup Complete ==="
echo "Rust: $(rustc --version)"
echo "Solana: $(solana --version)"
echo "Anchor: $(anchor --version)"
echo "Node: $(node --version)"
