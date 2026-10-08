# Brief

## Problem

Copilot's "Auto" agent mode picks which agent answers a message. Today that choice is one
hard-coded step that developers cannot change:

- `StreamAgentAGUIController` passes only the **text of the last user message** to
  `IAIAgentService.SelectAgentForPromptAsync`.
- The service narrows the agents to the ones that are active and allowed in this context
  (`AIAgentScopeValidator`). Then it asks the classifier profile to pick one from each agent's
  name and description.
- If the classifier fails, or no classifier/default chat profile is set up, it silently picks the
  first agent. Nothing records why an agent was picked.
- The frontend sends the `auto` agent ID on **every** turn (`run.controller.ts`), so the agent is
  re-picked on every message and can change mid-conversation. The selection step has no idea what
  it picked last time.

A customer/partner has asked to drive the choice with **their own business rules**: logic only
they know, such as user group, site, content type, or the entity being edited. Today the only
lever is editing agent descriptions and hoping the LLM classifier agrees. That is not
deterministic, can't see anything beyond the message text, and costs an LLM call for decisions a
simple `if` could make.

**Who it's for:** developers integrating Umbraco.AI into a site or package, who want to add or
replace the logic that picks the agent. Not editors or admins: there is no backoffice UI for this
in scope. Editors keep using "Auto" exactly as today.

**Why now:** a real customer/partner request for business-rule-driven agent selection.

**Success looks like:**

- A developer can register their own selection logic in a composer, without forking or replacing
  `AIAgentService`. That logic can see the full conversation (history, attachments), the
  availability context (surface, section, entity), the current user, the frontend tools, and the
  agent picked on the previous turn.
- A developer's logic can decide, or pass so the next piece of logic decides. The current LLM
  classifier still runs as the default when nothing else decides.
- Out of the box, behaviour is unchanged. With no custom logic registered, auto mode picks the
  same agent it picks today.
- Custom logic can only choose from agents the scope rules already allow. It can never be used to
  reach an agent the surface/context ruled out.
- 1-2 simple built-in rules ship with it (e.g. route by attachment type) to prove the extension
  point is enough for real rules, not just the LLM one.
- The reason for each pick is recorded in the `agent_selected` AG-UI event and in the audit log.
  This includes the "fell back to the first agent" case, which is silent today.
- No public API break. `SelectAgentForPromptAsync` keeps working.

**Constraints:**

- Build on `v18/dev` first, then backport to `v17/dev`. Both lines are in active support.
- Public API backwards compatibility: anything replaced stays, proxies to the new path, and is
  marked `[Obsolete("Will be removed in v20")]`.
- Code-only extension point (developer audience). No new backoffice UI.

**Riskiest unknowns:**

- **Stickiness vs today's behaviour.** Agreed direction: pass the previous pick into selection so
  each rule can choose to stick or switch, with the default keeping today's re-pick-every-turn
  behaviour. TODO: confirm the previous pick is reliably available server-side on each turn (only
  the frontend tracks it today, via `resolvedAgent$`), or whether the frontend must send it back.
- **Interaction with starter prompts.** The starter-prompts feature pins a conversation to the
  starter's agent. TODO: check whether that pin bypasses `auto` entirely or runs through
  selection, so the two don't contradict each other.
- **Audit log shape.** TODO: confirm the existing audit log has somewhere sensible to record a
  selection reason. Agent selection happens before the agent run's audit entry exists.
- **What "the customer's business rules" need to see.** No concrete example rules are available.
  The working assumption is that rules are based on context: the availability context (surface,
  section, entity), the current user, and the conversation. So selection must expose that context
  in full rather than a curated subset.

## Non-goals

- **Profile routing (picking the model per request)** is a separate idea, kept in
  `docs/ideas/pluggable-routing.md` Part 2. This feature only picks the agent, never the model.
- **Backoffice UI for configuring selection rules.** The audience is developers only. A rule
  editor (e.g. modelled on guardrails) can come later if admins ask for it.
- **Changing scope/availability rules.** Selection runs after scope filtering and never replaces
  it.
- **Changing the explicit-agent path.** Picking a named agent in Copilot is untouched. This only
  affects `auto`.
- **Forcing pick-once stickiness.** Stickiness is left to each rule. The default keeps today's
  behaviour.
