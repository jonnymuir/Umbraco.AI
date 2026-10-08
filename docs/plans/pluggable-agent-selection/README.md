# Pluggable Agent Selection

Read these in order:

1. [x] **[BRIEF.md](BRIEF.md)** — Developers can't plug their own business rules into Copilot's `auto` agent pick; this makes it a code-only extension point with unchanged default behaviour.
2. [x] **[ARCHITECTURE.md](ARCHITECTURE.md)** — An ordered `AIAgentSelectors()` collection run by a new `IAIAgentSelectionService`, after scope filtering, with an LLM default, opt-in sticky selector, and an `AIAgentSelectedNotification`.
3. [x] **[SPEC.md](SPEC.md)** — No new routes; the `auto` stream reads `forwardedProps.previousAgentId`, adds `selectorId`/`reason` to `agent_selected`, and records both in audit metadata.
4. [x] **[STORIES.md](STORIES.md)** — 8 stories (S1-S5 core, S6 docs placeholder, S7-S8 Copilot Workspace).
5. [x] **[PLAN.md](PLAN.md)** — 17 tasks; T1-T12 done, T13-T17 add Copilot Workspace (merge v18/dev, message AgentId, wiring, names, live check).
6. [x] **[BUILD-LOG.md](BUILD-LOG.md)** — 17 tasks done (T11, T12, T17 are live demo-site checks); Copilot Workspace wired in after merging v18/dev. Agent, Workspace and Automate suites green.

See [DECISION-LOG.md](DECISION-LOG.md) for why things changed along the way.
