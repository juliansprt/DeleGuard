# Verified Paper Corpus

Verified/public-paper manifest: **38 papers**.

The PDFs are public arXiv URLs. Run `scripts/Download-Papers.ps1` to materialize them locally.

| arXiv | Category | Title | Product relevance |
|---|---|---|---|
| [2608.27146](https://arxiv.org/abs/2608.27146) | authorization | When Tool Outputs Become Commands: Separating Action Induction from Runtime Authorization in Tool-Augmented LLM Agents | Critical - direct basis for separating induction from authorization |
| [2607.29167](https://arxiv.org/abs/2607.29167) | authorization-memory | Memory Provenance Laundering in LLM Agents: A Non-Amplification Firewall for Persistent Memory | Critical - provenance/authority non-amplification |
| [2608.01679](https://arxiv.org/abs/2608.01679) | authorization-memory | When Memory Becomes Authority: Benchmarking Authority Collapse at the Memory Consolidation Boundary | Critical - authority metadata must survive memory consolidation |
| [2608.16032](https://arxiv.org/abs/2608.16032) | evidence | Proof-of-Execution Memory: Defending LLM Agents Against Forged-Reasoning Attacks by Verifying What Actually Happened | Critical - trusted execution ledger / evidence |
| [2608.28542](https://arxiv.org/abs/2608.28542) | evidence | Offline-Verifiable Accountability for Cross-Organization Agent Messaging: A Preserved Evidence-Bundle Approach | High - offline-verifiable delegation/evidence bundles |
| [2608.23078](https://arxiv.org/abs/2608.23078) | tool-routing | AgentWeave: Routing Before Reasoning for Efficient Function Calling in Tool-Rich Language Models | High - policy-first candidate filtering, explicit authorization boundary |
| [2609.01222](https://arxiv.org/abs/2609.01222) | security | What's in Your Agent's Context? Context Privilege Escalation Attacks against AI Agent Harness | High - context privilege boundaries |
| [2609.03884](https://arxiv.org/abs/2609.03884) | security | A Blind Trust, the Bloody Thrust: When Attacker-Controlled Hook Updates Steer AI Agent Harnesses towards Malicious Behaviors | High - supply-chain/lifecycle hook risk |
| [2609.07360](https://arxiv.org/abs/2609.07360) | security | Scanning the Harness: An Empirical Study of Supply-Chain Defects in AI Coding-Agent Configurations | High - real configuration/supply-chain defects |
| [2609.11030](https://arxiv.org/abs/2609.11030) | security-evals | The Agent Incident Registry: Toward Preventing Repeated AI Agent Failures | High - incident taxonomy and eval coverage |
| [2608.25593](https://arxiv.org/abs/2608.25593) | harness | JIT-Agent: Scaling Harness Intelligence via Just-in-Time Harness Evolution | Medium/High - reinforces harness as separable capability |
| [2609.02889](https://arxiv.org/abs/2609.02889) | harness | Where Does Harness-Optimization Value Live? Localized Gains and the Budget-Splitting Trap in Self-Evolving LLM Agents | Medium - optimize high-credit control components |
| [2609.01481](https://arxiv.org/abs/2609.01481) | harness-long-horizon | Harness-of-Harness: Multi-Day Autonomous Software Development with Continual Improvement | High - planning/coding/testing/checkpoints over long tasks |
| [2609.00196](https://arxiv.org/abs/2609.00196) | harness-training | WHALE: A Simple Recipe for Joint Harness-Weight Optimization | Medium - model and harness are co-dependent |
| [2609.04280](https://arxiv.org/abs/2609.04280) | harness-evals | EVOHARNESSBENCH: Can Your Agents Keep Pace with an Evolving Harness? | Medium/High - added capabilities can degrade old behavior |
| [2609.02749](https://arxiv.org/abs/2609.02749) | skills | Repo-To-Skill: Distilling GitHub Repositories Into AI4AI Skills | Medium - operational knowledge as skills |
| [2608.29606](https://arxiv.org/abs/2608.29606) | memory | Agent Zero Memory: Provenance-Aware Long-Term Memory for LLM Agents | High - provenance/citation-lock memory architecture |
| [2609.09115](https://arxiv.org/abs/2609.09115) | memory | MeClear: Cooperative Game-Theoretic Attribution and Risk-Aware Memory Clearance for Long-Horizon LLM Agents | Medium/High - harmful memory filtering |
| [2606.24775](https://arxiv.org/abs/2606.24775) | memory | Are We Ready For An Agent-Native Memory System? | Medium - comparative memory-system decomposition |
| [2606.05684](https://arxiv.org/abs/2606.05684) | memory | AdaMEM: Test-Time Adaptive Memory for Language Agents | Medium - adaptive strategy memory |
| [2609.04915](https://arxiv.org/abs/2609.04915) | memory-efficiency | Compact-Memory LLM Agents via Online Max-Member Clustering and Atom-Aware Packing | Medium - quality/token tradeoff |
| [2609.02892](https://arxiv.org/abs/2609.02892) | verification-feedback | Counterexamples as Feedback for Agent Self-Correction | High - concrete verifier feedback beats generic retry |
| [2609.00434](https://arxiv.org/abs/2609.00434) | evaluation | SAGE: State-Grounded, Abstention-Aware Evaluation of Task-Oriented Dialogue Agents | High - deterministic/state-grounded verification |
| [2608.04975](https://arxiv.org/abs/2608.04975) | evaluation | SciCode-Verified: How Benchmark Defects Underestimated the Scientific-Coding Ability of Language Models | Medium/High - evaluator quality is a system risk |
| [2609.08943](https://arxiv.org/abs/2609.08943) | evidence-grounding | Evaluating and Improving Evidence-Grounded Fact-Checking in LLMs via Multi-Round Evidence Ablation | Medium - REAL/FAE tests whether outputs depend on evidence |
| [2609.08572](https://arxiv.org/abs/2609.08572) | multi-agent | AgentGrad: Intervention-guided Prompt Optimization for Multi Agent Systems | Medium - identify causal failing agent before optimizing |
| [2609.05774](https://arxiv.org/abs/2609.05774) | multi-agent | Inference-Time Graph Engineering for Multi-Agent LLM Workflows | Medium - dynamic communication topology (ReActNet) |
| [2609.09565](https://arxiv.org/abs/2609.09565) | multi-agent | Multi-Agent Agentic Graph Learning via Structural Signatures | Low/Medium - selective collaboration and confidence |
| [2609.04170](https://arxiv.org/abs/2609.04170) | multi-agent-safety | A Case Study on Emergent Cheating and Whistleblowing in Autonomous Research Swarms | Medium - shared infrastructure propagates bad/good behaviors |
| [2609.03335](https://arxiv.org/abs/2609.03335) | operations | Latency-Aware Orchestration for Multi-Agent LLM Workflows on Heterogeneous GPUs | Low for MVP / useful later - scheduling economics |
| [2608.13900](https://arxiv.org/abs/2608.13900) | reliability | Agentic Transaction: Towards ACID-Compliant Agent Systems | High - transactional semantics for side effects |
| [2608.27992](https://arxiv.org/abs/2608.27992) | operations | GOD: Govern, Observe, and Direct - A Real-Time Control Room for Agent Societies | Medium - operator control plane/observability |
| [2609.04304](https://arxiv.org/abs/2609.04304) | search-context | Iris: Climbing to the Search Frontier | Low for MVP - evidence that context management matters |
| [2609.08444](https://arxiv.org/abs/2609.08444) | safety | Safe Task Planning with Long-Term Graph Memory for Embodied Agents | Low for MVP - risk-aware replanning pattern |
| [2609.08965](https://arxiv.org/abs/2609.08965) | testing | PlannerForge: LLM Agents for Scenario-Based Testing of Motion Planners in Autonomous Driving | Low for MVP - end-to-end agentic test/optimization loop |
| [2609.11225](https://arxiv.org/abs/2609.11225) | runtime | Harness Robotic OS: A Unified Embodied-Agent Runtime for Closed-Loop Quadruped Inspection | Low for MVP - gated self-evolution and traceable runtime |
| [2609.10430](https://arxiv.org/abs/2609.10430) | governance | Glyph: A Multi-Strategy Agentic System for Column Description and Sensitivity-Ontology Tagging of Enterprise Data Catalogs | Low/Medium - provenance and mixed deterministic/ML strategies |
| [2609.11190](https://arxiv.org/abs/2609.11190) | agent-commerce | Agentic Share-of-Search: A Multi-Agent AI System for Competitive Decision-Making in LLM-Mediated E-Commerce | Low for MVP - example of agents influencing commercial actions |
