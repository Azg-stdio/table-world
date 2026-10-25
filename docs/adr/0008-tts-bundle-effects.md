# ADR 0008 — Play TTS asset-bundle effects through a stand-in for TTS's effects component

Status: **accepted** (2026-10-25, research spike for G35; implemented the same day after Miguel's go-ahead).

## Context

Both Pokémon mods animate their figures through TTS's effect API: `playLoopingEffect` (920 calls) and
`playTriggerEffect` (604 calls). Today the bundles load (ADR 0001), but the figures stand still and the calls do nothing.

The effect lists live in a TTS component inside each bundle. Unity sees it as a missing script, so the question was
whether its data can be read at all.

**Spike:** we read 40 Master Trainer bundles (of 1 026) and 20 Sinnoh bundles (of 901) with UnityPy 1.25.3, a
Python reader for Unity files. The script and its numbers are in the "Spike record" section below.

| Finding | Detail |
|---|---|
| Every sampled bundle has TTS's component | 60 of 60. The script reference is `Assembly-CSharp.dll`, no namespace, class `TTSAssetBundleEffects` |
| Its field layout is stored in the bundle | So the data is readable, field by field |
| Fields | `LoopingEffects[]` and `TriggerEffects[]`, each entry with `Name`, `GameObjects[]` (shown while playing), `Particles[]`, `Sound { Audio, Positional3D }`, `Animation { AnimationComponent, AnimationName }`, `Animator { AnimatorComponent, StateName }`; trigger effects add `Duration` |
| Typical Pokémon entries | Looping: Idle, Walk/Move, Run, Faint. Trigger: Physical Attack, Hidden Power, Take Damage |
| How they animate | Legacy `Animation` components (one per bundle) holding about 8 clips each (`AnimationName` = "ZZ1", "1" …), skinned meshes; an `Animator` reference is present but `StateName` is empty in the samples |
| Particles and sounds | None in the sample (the lists are empty); other mods may use them |

## Decision (proposed)

Add a stand-in class with the same identity, so Unity reads the data for us when a bundle loads:
- **The class:** `TTSAssetBundleEffects`, with no namespace, compiled into `Assembly-CSharp`. It goes in a folder
  without an asmdef, `Assets/_Project/TtsCompat/`.
- **Its fields:** the same serialized fields, re-declared from the layout above. TTS's code is not copied: only names
  and types, which interoperability needs.
- **Bridge:** `Assembly-CSharp` can reference our `TableWorld.Runtime` assembly (the reverse is not allowed). The
  stand-in therefore implements an interface defined in the runtime, `ITtsBundleEffects`, and the runtime finds it
  with `GetComponent<ITtsBundleEffects>()`.
- **Playing an effect:** play the clip (`Animation.Play(AnimationName)`, or `Animator.Play(StateName)`), show its
  `GameObjects`, play its particles and sound. A trigger effect returns to the looping effect after `Duration`.
- **Start:** a figure starts in looping effect 0, as in TTS.
- **Lua:** `playLoopingEffect(i)`, `playTriggerEffect(i)`, `getLoopingEffects()` and `getTriggerEffects()` read the
  real lists.
- **Multiplayer:** the effect index travels as a small table command, so every machine plays the same effect.

## Consequences

- The Pokémon figures idle, walk, attack and faint as in TTS. The 920 + 604 effect calls, and `getLoopingEffects` /
  `getTriggerEffects` in the coverage list, start working.
- One file lives outside our asmdefs. The class name is fixed by TTS's bundles and must never be renamed.
- Size: medium. The stand-in, the effect player, a command, the Lua calls, and a test with a local bundle (skipped
  without a TTS cache, like the other bundle tests).
- Checked: legacy `Animation` clips built with Unity 5.6 play in 6.6. A Paras bundle reads looping [Idle, Walk, Run,
  Faint] and trigger [Physical Attack, Special Attack, Status Attack, Take Damage]; a Master Trainer figure idles on
  the table. `BundleEffectTests` covers it with the first cached bundle that has effects.
- The chosen looping effect is saved on the object (`loopingEffect`, imported from `LoopingEffectIndex`); a trigger
  effect is an event only (`EffectTriggered`), so it is not replayed on load.

## Spike record (2026-10-25)

`bundle_probe.py`, run in an isolated environment with UnityPy 1.25.3.
- Master Trainer, 40 bundles: 2 302 GameObjects, 315 AnimationClips, 306 SkinnedMeshRenderers, 40 Animation and 42
  Animator components, 40 effects components.
- Sinnoh, 20 bundles: 157 AnimationClips, 135 SkinnedMeshRenderers, 20 Animation and 21 Animator components, 20
  effects components.
