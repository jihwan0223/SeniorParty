# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume silently. But don't stall on questions either.**

Before implementing:
- Make the most reasonable assumption and proceed. Note key assumptions briefly at the end, only if they materially affect the result.
- Ask only when the request is genuinely ambiguous AND a wrong guess would waste real work. Otherwise pick the best interpretation and go.
- When multiple approaches exist, pick the single most appropriate one and apply it in full — don't list options and wait.
- Don't reintroduce an approach that was already rejected earlier in the session.
- If a simpler approach exists, say so and use it.

Before touching any tool or writing code:
- Plan first. Know the goal and the steps before acting.
- Tools cost tokens. Before each tool call ask: is this the right tool for this problem? Is it necessary at all?
- Prefer the cheapest tool that answers the question (dedicated file/search tools over shell; targeted reads over dumps; no redundant re-reads).
- Don't fire tools to explore aimlessly — call a tool only when you know what you're looking for and why.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Don't rename established variables, methods, or fields without being asked.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Verify before calling it done.**

Transform tasks into verifiable goals. Verification means an automated test OR a play-mode / visual check in the Unity editor — whichever actually fits the code:

- Pure logic (physics math, orbit prediction, save/load, data handling) — write or update a test, then make it pass.
- Gameplay, feel, or UI (movement, tilt-based gravity, spawning, visuals) — describe the concrete play-mode check to run in the editor. Don't force unit-test scaffolding onto code that can only be judged by playing it.
- "Fix the bug" — reproduce it first (a test, or clear repro steps in the editor), then confirm the fix removes it.

For multi-step tasks, state a brief plan with a verify step for each:
```
1. [Step] — verify: [test or play-mode check]
2. [Step] — verify: [test or play-mode check]
3. [Step] — verify: [test or play-mode check]
```

## 5. Quiet Execution, Answer in the Fewest Words

**Do the work first. Answer in words/fragments, not sentences.**

DEFAULT ANSWER FORMAT — applies to EVERY reply in EVERY session, not just end summaries:
- Answer in single words, fragments, or short bullets. NOT full sentences.
- Only the genuinely important part gets full sentences — everything else stays terse.
- No preamble ("Sure!", "Got it", "Here's what I did"), no filler, no restating the question.
- If a one-word answer is correct and complete, give one word.

- Don't narrate intentions before or between actions ("Now I'll open this file and fix the function..."). Skip the play-by-play prose.
- Just run the tool calls. Tool calls are already visible; don't wrap them in explanatory sentences.
- Skip internal reasoning/deliberation output entirely if it's not needed by the user.
- End summaries: fragments/bullets, outcome-focused. Full sentences only for what actually matters.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, fewer needless clarifying questions on clear requests, and verification that matches how the code is actually exercised.
