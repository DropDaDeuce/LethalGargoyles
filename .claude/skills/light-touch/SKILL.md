---
name: light-touch
description: Drop to a cheaper, lower-effort tier for routine bookkeeping — appending a dated entry to the current month's Docs/Archive/Done/Done_YYYY-MM.md, ticking or removing an item in ToDo.md, adding a line to CHANGELOG.md under an existing release, claiming or releasing a row on the session board, filing an archive entry, or a plain lookup with no design judgment in it. Do NOT load this for authoring or revising a design doc, a plan, or CLAUDE.md, for anything under Plugin/src, and never mid-way through a task that is holding complex context.
paths:
  - ToDo.md
  - CHANGELOG.md
  - Docs/Session_Board.md
  - Docs/Archive/**
model: sonnet
effort: low
---

# Light-touch lane

Bookkeeping tier. Make the entry, match the surrounding format exactly, and stop.

The session-board protocol still applies: claim before editing, own your row end to end,
never ask Mathew to touch the board.

Two format rules that are easy to get wrong at this tier:

* **`Done_YYYY-MM.md` appends at the TOP**, newest first, and an entry has to carry something
  the code cannot tell you. "Fixed the thing" is not an entry.
* **`ToDo.md` is OPEN ITEMS ONLY.** A shipped item is removed, not ticked — it moves to the
  month's Done file and, if players will notice it, to `CHANGELOG.md`.

If the task turns out to involve real judgment — a decision, a design call, a non-obvious edit,
or a Done entry you'd have to reconstruct rather than transcribe — say so and let the session
default handle it rather than pushing through at this tier.
