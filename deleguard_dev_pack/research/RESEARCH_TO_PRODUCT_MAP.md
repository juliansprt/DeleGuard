# Research -> Product Map

This document translates the verified corpus into concrete Deleguard design decisions.

## 1. Authorization must be distinct from reasoning

**Papers:**
- When Tool Outputs Become Commands (SARA)
- Memory Provenance Laundering
- When Memory Becomes Authority

**Product implication:** A model/tool/memory can suggest an action but cannot create authority. Authorization uses trusted identity, explicit delegation, resource/context attributes and policy.

## 2. Evidence must describe what actually executed

**Papers:**
- Proof-of-Execution Memory
- Offline-Verifiable Accountability

**Product implication:** add an evidence API and immutable receipts. Do not accept "the agent says it succeeded" as strong evidence.

## 3. Tool visibility and authorization are different

**Paper:** AgentWeave.

**Product implication:** optional routing/filtering may reduce tool exposure, but a fail-closed authorization gate still sits immediately before execution.

## 4. Agent configuration is a supply-chain surface

**Papers:** Context Privilege Escalation, HookPry, Scanning the Harness, Agent Incident Registry.

**Product implication:** never infer permissions from skill/tool descriptions. Add version identifiers to agent/action metadata and audit incidents by surface.

## 5. Long-running workflows need transactional semantics

**Papers:** Agentic Transaction, Harness-of-Harness.

**Product implication:** later add action groups, compensation metadata, checkpoints and transaction-like state. MVP should at least support idempotency and proof of final execution state.

## 6. Verification should be grounded

**Papers:** SAGE, Counterexamples as Feedback, SciCode-Verified, REAL/FAE.

**Product implication:** policy simulation must be deterministic and return concrete counterexamples. Audit the evaluator itself. Evidence-aware tests should change when underlying evidence changes.

## 7. Memory/harness intelligence is adjacent, not MVP

**Papers:** Agent Zero Memory, MeClear, AdaMEM, JIT-Agent, HARNESSEVO, WHALE, EvoHarnessBench.

**Product implication:** integrations can later ingest provenance/risk signals from memory/harness systems, but Deleguard should not become a memory/harness product in its first wedge.
