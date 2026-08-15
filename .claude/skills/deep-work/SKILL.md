---
name: deep-work
description: Arm deeper reasoning for genuinely hard engineering work in this mod — reworking the AI state machine or pathing, changing the audio transfer/networking path, adding a new networked mechanic, a multi-gargoyle coordination problem, or debugging a defect whose cause is not yet identified. Do NOT load this for routine work — reading code, answering questions, adding a voice line to an existing category, a single config-value edit, applying a change already designed, or anything already understood.
paths:
  - Plugin/src/**
  - UnityProject/Assets/Scripts/**
model: fable
effort: max
---

# Deep work lane

Reasoning is running at a higher tier for this turn. It reverts on the next prompt.

Earn it — this lane exists for problems where the cause or the design is genuinely
unresolved, not for volume. If the task turns out to be mechanical, just do it and don't
spend the depth.

Repo rules still apply in full: `CLAUDE.md` is law. In particular for anything in this lane:

* **Server decides, clients play.** Gameplay logic runs behind `IsServer`/`IsOwner`; the
  client half only renders audio and animation. A design that moves a decision client-side
  is wrong here even when it looks simpler.
* **A new `[ServerRpc]`/`[ClientRpc]` or `NetworkBehaviour` depends on the netcode patcher**
  running post-build, and on `Plugin.InitializeNetworkBehaviours()`. Say so explicitly when
  your change adds one — it is the single most common cause of "it compiled and did nothing".
* **Per-frame cost is a real constraint** — several gargoyles can be alive at once. Add work
  to the existing throttled paths (`AGGRO_EVAL_INTERVAL`, `HIDE_EVAL_INTERVAL`) and the
  existing static caches, not to raw `Update` and not to a fresh `FindObjectsOfType`.
* **Cross-instance state is static and concurrent** (`gargoyleTargets`, `playerPushStates`,
  `activeGargoyles`). Anything you add there must tolerate another instance mutating it mid-tick.
* Check `Docs\Session_Board.md` before your first edit, and **claim `BUILD` before you build** —
  the output folder and the dev profile are shared.
* You can build; you cannot test. Finish by telling Mathew what to watch or listen for in game.
