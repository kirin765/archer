# Milestone 1–2 — Practice tutorial + Chapters One & Two (10 raids)

Goal: a playable archery tutorial followed by two five-raid chapters, with clearer
feedback and the instrumentation needed to tune the defense.

## What shipped

**1. Playable practice tutorial** — `Scripts/Core/TutorialDirector.cs`
Opens before raid one (first run only). Three steps, free arrows, no damage taken:
1. Hit a stationary target twice.
2. Hit a moving runner (learn to lead).
3. Hit the exposed head above a shield (body shots are blocked and coached).

The intro screen starts practice on first launch; **Practice first / Skip practice**
are always available. Entering practice forces the trajectory guide on.

**2. Distinguishable misses** — `Arrow.cs`, `GameFeedback.cs`, `GameManager.MissReason`
Every failed shot is classified and named:
- **SHIELD BLOCKED** — stopped by a shield.
- **FELL SHORT** — landed before the enemy line (draw longer / aim higher).
- **MISSED** — travelled wide or past the target (lead the target).

Run results now also list blocked / short / wide counts. The trajectory guide is on
by default for beginners (`LastRanger.Guide` default 1). On touch, the first tap
places the shot above the finger and further dragging adjusts the aim relatively,
so the reticle is not tied to the thumb.

**3. Authored raids** — `Scripts/Core/RaidPlan.cs`
Raids are now hand-authored schedules (role, lane, distance, delay) rather than
counts + stats:
1. FIRST SIGNS — single-file raiders to build confidence.
2. THE RUSH — runners break ahead of the line; learn to lead.
3. WOODEN SHIELDS — a shield leads, a body follows in its shadow.
4. THE AMBUSH — two staggered lines; each shield has a runner arriving behind it.
5. THE BRUTE — ogre headlines behind two shields. Headshots interrupt his swing
   (HUD shows a live countdown: `ATTACK IN x.x s · HEADSHOT TO INTERRUPT`).
6. THE SECOND WAVE — chapter two opens with a wider, faster mixed push.
7. IRON RANKS — a shield wall with runners using the gaps.
8. THE HUNT — runners dominate; they must be led before the shields close.
9. THE BRUTE'S GUARD — shields arrive first; the brute follows the wall.
10. THE WARDEN — the brute leads the last wave; the guard follows.

The loop still extends past ten (a scaling horde) if playtests ask for more.

**4. Shot-changing upgrades** — `UpgradeKind.Piercing / Slowing / HeadshotRefund`
Offered as a meaningful alternative alongside the numeric upgrades (one is surfaced
whenever available):
- **Bodkin points** — arrows pierce the first enemy and keep flying.
- **Hamstring shot** — hits slow the target.
- **Recover** — headshot kills return one arrow.

**5. Two complete chapters**
`GameManager.RaidsPerChapter = 5` and `TotalRaids = 10`. Raid five ends chapter one
and opens the chapter-two supply screen; raid ten clears chapter two with full run
stats. The short pause after the final kill keeps the intermission card from
snapping over the battlefield immediately.

**6. Telemetry** — `Scripts/Core/Telemetry.cs`
JSON lines appended to `Application.persistentDataPath/last-ranger-telemetry.jsonl`:
`run_start`, `raid_start`, `raid_clear` (with per-raid shots/hits/accuracy/seconds),
`first_hit` (seconds to the player's first successful shot), `upgrade` (which supply
was chosen, per raid), `run_end` (victory/death), `quit` (abandoned runs).

## Verifying

- `Archer ▸ Run Tutorial and Feedback Checks` — 40 assertions covering the tutorial
  flow (including one real ballistic shot), miss classes, the three upgrades, piercing
  physics, touch-aim drag, authored raid structure, chapter mapping, and telemetry.
  Run in a fresh Play session (before a raid).
- `Archer ▸ Run Last Ranger Integration Checks` — 86 assertions (a couple are
  upgrade-dependent) covering the raid loop, physics, shields, upgrades,
  pause/death/restart, the intermission beat, and both chapter endings.

## Playtest plan (before tuning numbers)

Watch several new players without coaching, then read the telemetry:
- Time to `first_hit` and whether practice steps are cleared or skipped.
- Accuracy by raid (`raid_clear`) — where it drops signals a spike, not a slog.
- Upgrade picks (`upgrade`) — are shot-changing choices taken, or ignored for numbers?
- Deaths and `quit` raids — which raid sheds players, and whether chapter two holds them.
- Only then decide on difficulty and whether to extend past ten raids.
