#!/bin/bash
# Helper to run commands with Solana/Anchor tools in PATH
# Usage: wsl -d Ubuntu -- bash /mnt/e/Github2/SeekerDungeon/solana-program/scripts/wsl/run.sh "command here"

export PATH="$HOME/.local/share/solana/install/active_release/bin:$HOME/.cargo/bin:$PATH"
source "$HOME/.cargo/env" 2>/dev/null || true

cd /mnt/e/Github2/SeekerDungeon/solana-program
eval "$@"
