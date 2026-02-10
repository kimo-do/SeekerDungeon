# MWA Multi-Signer Transaction Bug

## Problem

On Android, transactions requiring **two signers** (e.g. `begin_session` with wallet + session keypair) fail with:

```
invalid transaction: Transaction failed to sanitize accounts offsets correctly
```

The same transactions work perfectly in the Unity Editor.

## Root Cause

The Solana.Unity-SDK's `Transaction` class has two bugs that only manifest with multi-signer transactions on Mobile Wallet Adapter (MWA):

### Bug 1: Message recompilation

`Transaction.CompileMessage()` rebuilds the message from scratch on every call. This is called by `Sign()`, `PartialSign()`, and `Serialize()`. When the MWA wallet's `Account` has a dummy private key (no real secret key available on the client side), the recompilation produces incorrect account layouts, corrupting the message.

### Bug 2: Signature ordering

`Transaction.Serialize()` writes signatures in list-insertion order (the order they were added to `Signatures`), not in the strict order required by the Solana wire format (message account-key order: fee payer first, then other signers). When Phantom signs the transaction, it replaces the wrong signature slot.

### Why it works in editor

In the editor, the wallet is an `InGameWallet` with a real private key. `Sign()` and `PartialSign()` produce valid signatures even after recompilation because the keypair is fully available. On MWA, the wallet's `Account` is a stub (public key only), so the recompiled message + re-signing produces garbage.

## Fix (PrecompiledTransaction)

The fix lives in `LGWalletSessionManager.SendMultiSignerTransactionViaWalletAdapterAsync()`:

1. **Use `TransactionBuilder.Build()`** to produce the initial wire-format bytes. `TransactionBuilder` handles account sorting and instruction index mapping correctly (battle-tested, used by CandyMachine, etc.).

2. **Extract the message bytes** from the built transaction by parsing past the compact-u16 signature count and signature slots.

3. **Parse signer order** from the message via `Message.Deserialize()` to know which public key maps to which signature slot.

4. **Wrap in `PrecompiledTransaction`** -- a subclass of `Transaction` that overrides:
   - `CompileMessage()` to return the fixed message bytes (no recompilation)
   - `Serialize()` to write signatures in message-account order (not insertion order)

5. **PartialSign** with local signers (session keypair) against the correct message bytes.

6. **Send to Phantom** via `wallet.SignAllTransactions()`. The SDK internally calls `PartialSign(walletAccount)` then `Serialize()` -- both now use the overridden methods, so Phantom receives correct bytes.

7. **Manually assemble final bytes** after Phantom returns. Take the signatures from Phantom's response and combine with the known-good message bytes using `AssembleTransactionBytes()`. This completely bypasses `Transaction.Serialize()` on the returned `Transaction` object (which would recompile and break things).

8. **Send raw bytes** to the RPC as base64.

## Session Signer Funding

A separate issue: the session signer keypair needs SOL to pay gameplay transaction fees. On MWA, a separate `wallet.Transfer()` call would trigger a second wallet popup (bad UX). The fix bundles a `SystemProgram.Transfer` instruction into the same `begin_session` transaction, so the user only sees one wallet approval.

This is controlled by the `shouldBundleTopUp` logic in `BeginGameplaySessionAsync()`, which forces bundling when `ActiveWalletMode == WalletAdapter`.

## Key Files

- `Assets/Scripts/Solana/LGWalletSessionManager.cs` -- all fix code lives here
- `PrecompiledTransaction` inner class -- the core workaround
- `AssembleTransactionBytes()` -- manual wire-format assembly
- `DecodeCompactU16()` -- helper for parsing compact-u16 values

## Relevant SDK Source (for reference)

- `Solana.Unity-Core/src/Solana.Unity.Rpc/Models/Transaction.cs` -- buggy `CompileMessage()` and `Serialize()`
- `Solana.Unity-Core/src/Solana.Unity.Rpc/Builders/TransactionBuilder.cs` -- correct `Build()` implementation
- `Solana.Unity-Core/src/Solana.Unity.Rpc/Models/Message.cs` -- `Message.Deserialize()`

## How the Lumberjack Example Avoids This

The official Solana game example (solana-game-examples/lumberjack) uses MagicBlock's gpl-session `SessionWallet`. After session creation, all gameplay transactions are signed solely by the session wallet (single signer), avoiding the multi-signer path entirely. Their session creation code (`PartialSign` both + `SignAndSendTransaction`) has the same vulnerability but is likely only tested in the editor/browser.
