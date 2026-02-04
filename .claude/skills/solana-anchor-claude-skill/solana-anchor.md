---
name: solana-anchor-research-agent-skill
description: Research-driven agent skill for building modern Solana programs with Anchor using up-to-date best practices, verified via live internet sources.
---

# Anchor Program Builder Agent (Solana)
**Research-driven · Production-grade · No legacy patterns**

## Mission

Build production-grade Solana **programs** using the **latest stable Anchor**, guided by **recent, verified best practices** from official documentation and current ecosystem examples.

This agent **must actively research the internet** and **must not rely solely on inherent or historical knowledge**, as Anchor, Solana tooling, and best practices evolve rapidly.

---

## Success Criteria

- Program builds cleanly
- IDL is generated
- Tests pass
  - If `npm test` exists: **must pass**
  - Anchor integration tests must pass if present
- No success may be declared until all tests pass

### Work Summary Format

- ✅ Completed work items
- ❌ Remaining work items

---

## Mandatory Research Workflow (Non‑Optional)

Before implementing or modifying anything non-trivial (PDAs, account constraints, CPI, token logic, space calculation, clients, deployment, upgrades):

1. **Search the internet** for recent (<12 months preferred) guidance and examples
2. Prefer **official or primary sources**
3. Cross-check examples against current Anchor behavior
4. Maintain short **evidence notes** (links) justifying key decisions

**Never assume older Anchor patterns are still valid.**

---

## Approved Documentation Sources

Use **only** the following sources unless explicitly approved otherwise:

- **Anchor** – https://www.anchor-lang.com/docs
- **Solana Kite** – https://solanakite.org
- **Solana Kit** – https://solanakit.com
- **Agave (Solana CLI by Anza)** – https://docs.anza.xyz
- **Switchboard** (if used) – https://docs.switchboard.xyz/docs-by-chain/solana-svm
- **Arcium** (if used) – https://docs.arcium.com/developers
- **Codama** (IDL → client generation)

---

## Explicitly Disallowed

- Solana Labs documentation (superseded by Anza)
- Project Serum documentation or tooling
- Yarn — **use npm only**
- Legacy web3.js v1 patterns
- Historical Anchor examples without verification

---

## Library Versions

Always use the **latest stable** versions of:

- Anchor
- Rust
- TypeScript
- Solana Kit
- Solana Kite

If a bug occurs, **prefer upgrading over downgrading**.

---

## General Coding Guidelines

### You Are a Deletionist

Perfection is achieved when there is **nothing left to remove**.

Remove:
- Redundant comments
- Repeated code that should be a function
- Dead files, imports, constants

---

### Code Honesty

Never mislead the reader:

- Names must match behavior
- Instructions ≠ transactions
- Workarounds must be labeled with `TODO(<condition to remove>)`

---

### Variable Naming

Prefer clarity over brevity.

**Bad**
```ts
// Foo
const shlerg = getFoo();
```

**Good**
```ts
const foo = getFoo();
```

Rules:
- Arrays are plural, items singular
- Functions are verbs
- No abbreviations (`context`, not `ctx`)
- Never throw or name something `e`
- Instructions must be named as instructions

---

### Code Quality Rules

- Extract repeated logic into functions
- Avoid magic numbers
- If values come from an IDL, **import the IDL and derive them**
- Production code only — no hypothetical comments
- Do not delete useful or accurate existing comments

---

## TypeScript Guidelines

### General

- Avoid `tsconfig.json` unless required (explain why if used)
- Use modern ECMAScript (up to ~2023)

### Async

- Prefer `async/await` with `try/catch`
- Do not use `.then()` chains

### Types

- Always use `Array<T>` (never `T[]`)
- Never use `any`

---

### Solana-Specific TypeScript

- Do **not** create new web3.js v1 code
- Do **not** use `@coral-xyz/anchor` TS clients
- Use **Solana Kit**, preferably via **Solana Kite**
- Use Kite helpers for PDA derivation
- Generate clients from IDLs using **Codama**

---

### Unit Tests

- Place tests in `tests/`
- Use Node.js built-in test runner

```ts
import { before, describe, test } from "node:test";
import assert from "node:assert";
```

- Use `test`, not `it`

---

### Thrown Object Handling

```ts
// JS allows throwing anything; we normalize to Error
const ensureError = function (thrownObject: unknown): Error {
  if (thrownObject instanceof Error) {
    return thrownObject;
  }
  return new Error(`Non-Error thrown: ${String(thrownObject)}`);
};
```

---

## Rust Guidelines (Anchor Programs)

### Platform Awareness

- Solana **programs**, not smart contracts
- Transaction fees, not gas
- No mempools

Token terminology:
- **Token Extensions Program** (new)
- **Classic Token Program** (old)

Use **onchain / offchain**, never hyphenated.

---

### Anchor Version

- Code must match the **latest stable Anchor**
- Avoid unnecessary macros
- Validate behavior against the Anchor changelog

---

### Required Anchor Features

```toml
[features]
idl-build = ["anchor-lang/idl-build", "anchor-spl/idl-build"]
```

```toml
[dependencies]
anchor-spl = "<latest>"
```

---

### Project Structure

- Never change the program ID
- `state/` for account data
- `instructions/` or `handlers/` for logic
- Account constraint structs must end in `AccountConstraints`
- Admin-only logic goes in `admin/` subfolders

---

### Account Constraints

- One constraint per line
- Prefer declarative constraints over runtime checks

---

### Bumps

- Use `ctx.bumps.account_name`
- Store `pub bump: u8` in all PDA-backed accounts

---

### Data Structures

- All `String` and `Vec` fields must declare `max_len`
- Vectors require:
  - max items
  - max item size

---

### Space Calculation (CRITICAL)

- **No magic numbers**
- Do not hardcode sizes
- Use:
```rust
space = StructName::DISCRIMINATOR.len() + StructName::INIT_SPACE
```

- All stored structs must derive `InitSpace`

---

### Error Handling

- Return explicit, user-actionable errors
- Validate parameters early
- Guard against duplicate mutable accounts

---

### Clock

Use:
```rust
Clock::get()?;
```

---

## Git Commits

- Do not add AI attribution (e.g. Co-Authored-By)

---

## Required Acknowledgment

When starting work:

> **Acknowledged:** These Anchor/Solana guidelines are applied for this project.
