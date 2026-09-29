---
name: portfolio-generation
description: Generate portfolio project entries (keyMetric, description) or profile-level narrative (bio, summary, headline) in the user's voice. Grounded in brain.db identity/decisions, AGENTS.md, active plans, and live repo state. Enforces strict voice rules (no em-dashes, no diplomatic reframes, no fabricated capabilities, no vanity metrics, no scaffolding bleed).
allowed-tools:
  - Read
  - Bash
  - Glob
  - Grep
---

# /portfolio-generation — Portfolio & Profile Narrative Generator

Generate portfolio or profile copy in the user's voice, grounded in the running project's authoritative sources.

## When to Use

- Generating a new portfolio `keyMetric` or `description` for a project
- Writing a profile `bio`, `summary`, or `headline`
- Rewriting existing copy the user hands over for revision
- Typically invoked after `/session-closeout` when the agent has maximum working knowledge of the project

## Protocol

Read `.Codex/.prompts/portfolio-generation.md` for the full protocol. It covers:

- Hard voice rules (no em-dashes, no diplomatic reframes, no fabricated capabilities, no vanity metrics, no scaffolding bleed)
- Output formats (portfolio project entry JSON, profile bio/summary/headline JSON)
- Subject discipline (when scaffolding IS the product vs when it's just infrastructure)
- Grounding sources (brain.db identity, decisions, active plans, AGENTS.md, README, route registry)
- Grounding depth (specificity test — the sentence could only fit THIS project)
- Voice anchor (match the existing `product.summary` density and register)
- Voice guidelines (density over polish, quantified claims only, anti-hype vocabulary ban)
- Surface-specific rules (keyMetric, description, bio, headline each have their own register)
- Iteration protocol (when a draft is rejected, update the template before redrafting)
- Anti-patterns (em-dash flow, diplomatic reframing, filler-cutting, rhythm smoothing, stale metrics, generic tech names)
- Verification trace (every quantified claim must trace to a concrete source)

## Output

A JSON object matching the requested output format. Only include claims that trace to a concrete source (brain.db key, decision ID, live query, source file, AGENTS.md section).

## After Completion

- **Persist approved output to brain.db identity.** After user approval, run `dal.mjs identity set` for each approved field (`portfolio.keyMetric`, `portfolio.description`, or `profile.*` for bio/summary/headline). See "Persisting Output to brain.db" in the protocol file for the full flow and key mapping. Without this step, downstream Profile-sync consumers find empty identity keys and fall back to garbage extraction.
- **Record a trace** (`trace add decision_point "persisted portfolio-generation output: <keys>"`) so closeout and future sessions know identity was touched.
- If the draft was rejected and the correction generalizes beyond this session, save the lesson as a feedback memory (see Iteration Protocol in the protocol file).
- Do NOT persist rejected drafts. Do NOT overwrite fields the user did not explicitly approve.
