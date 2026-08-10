# Chop Chop Inc. Mods

BepInEx mods for **Chop Chop Inc.** (NullRef Entertainment).

Two plugins:

- **More Wood** — multiplies how many logs trees drop and how much wood logs drop, plus a
  whitelist-scoped stack multiplier.
- **Chop Chop Tweaks** — axe damage, money scaling, movement speed, crafting minigame skip and
  an item magnet. **Every setting defaults to vanilla**, so installing it changes nothing until
  you configure it.

## Game facts

| | |
|---|---|
| Engine | Unity **6000.3.5f2** (Unity 6.3) |
| Scripting backend | **Mono** (`ChopChopInc_Data/Managed/` contains real `Assembly-CSharp.dll`) |
| Loader | BepInEx 5.4.23.3 (x64, Unity Mono) + Doorstop 4.4.0 |
| Game code | `Assembly-CSharp.dll`, plus `GameFramework2.dll` (DI/service layer) |

Mono rather than IL2CPP is the good outcome: game types can be referenced directly at compile
time and patched with plain Harmony, with no IL2CPP interop or generated proxy assemblies.

## How drops actually work

Everything the game drops runs through one static method:

```csharp
// WorldObjects/SpawnOnDestroy.cs
public static void Spawn(IWorldObjectService worldObjectService,
                         SpawnObjectData[] spawns,
                         GameObject spawner)
```

`SpawnObjectData` is the interesting part — a serialized per-prefab table with `prefab`,
**`amount`**, spawn transform, random offset/rotation, a ground raycast, and a
`moveAlongSpline` flag (the arc drops fly along when they pop out of a log).

There are exactly three callers:

| Caller | When it fires | Mod's name for it |
|---|---|---|
| `SpawnOnDestroy.OnWorldObjectDestroy` | a `Destructible` reached 0 HP — tree → logs, log → wood | `OnDestroy` |
| `SpawnOnChangeHealth.OnHealthChanged` | every health change — usually per-axe-hit chips/debris | `OnDamage` |
| `Spawner.Tick` | timed regrowth of world resources | `Respawner` |

The chain is `Health` → `Destructible.onDestroy` → `SpawnOnDestroy` → `Spawn(...)`. So a
**single Harmony prefix on `Spawn`** covers trees, logs and respawners at once, and needs no
knowledge of individual prefabs.

## What More Wood does

A `[HarmonyPrefix]` on `SpawnOnDestroy.Spawn` rewrites the `spawns` argument before the
game's instantiation loop sees it.

Three details worth knowing:

- **The original table is never mutated.** `SpawnObjectData[]` belongs to a live component and
  is shared across instances of a prefab, so multiplying `amount` in place would compound on
  every single chop. Entries that change are cloned; the rest are passed through untouched.
- **Per-trigger multipliers.** `Spawn` is static and its arguments say nothing about who called
  it, so the three callers are each wrapped by a tiny patch that records the trigger
  (`SpawnTrigger.cs`). The value is restored in a `Finalizer`, so it unwinds correctly even if
  the game throws mid-spawn.
- **Anti-stacking scatter.** Many drops spawn at one exact point with no built-in randomness.
  One object is fine; a stack of five interpenetrates and the physics solver launches them
  across the map. When the game's own `randomOffset` is zero and we spawned extras, a small
  horizontal offset is added (`ScatterRadius`, default 0.35 m).

Fractional multipliers are honoured rather than rounded away: `1.5` on a single drop yields 1
or 2 with the correct long-run average.

## Setup

Prerequisites: .NET SDK 8, and the decompiler if you want to read game code:

```bash
dotnet tool install -g ilspycmd --version 9.1.0.7988
```

Then:

```bash
./scripts/fetch-bepinex.sh   # downloads BepInEx + ConfigurationManager into tools/ (gitignored)
./scripts/deploy.sh          # builds + installs BepInEx, the settings editor and the plugins
./scripts/decompile.sh       # optional: dump game C# into decompiled/ (gitignored)
```

If Steam is installed elsewhere, set `CHOPCHOP_DIR`:

```bash
export CHOPCHOP_DIR="/mnt/d/SteamLibrary/steamapps/common/ChopChopInc"
```

`scripts/deploy.sh` installs the BepInEx runtime only if `winhttp.dll` is missing; pass
`--with-bepinex` to force-reinstall it.

## Configuring

### In-game: press HOME

`scripts/deploy.sh` installs [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)
and pre-binds it to **HOME**. It reflects over every setting both plugins register and builds the
editor itself — there is no mod-side UI code to maintain, and nothing in it references game
assemblies, so a game update cannot break it.

Settings are grouped by the numbered section names (`1. Axe`, `2. Economy`, …) and each numeric
one renders as a slider bounded by its `AcceptableValueRange`. Edits apply immediately and are
written back to the `.cfg`.

Rebind it in the window itself under *Configuration Manager → Keyboard shortcuts*, or edit
`<game>/BepInEx/config/com.bepis.bepinex.configurationmanager.cfg`. Deploy only writes that file
when it is absent, so a rebind survives redeploys.

**Two settings ignore live edits:** `WalkSpeedMultiplier` and `RunSpeedMultiplier` are applied
once in `Move.Awake`, so they take effect on the next save load. Their descriptions say so in the
UI. Everything else reads its value at the point of use and responds instantly.

### By hand

Launch the game once to generate `<game>/BepInEx/config/chopchopmods.morewood.cfg`, then edit it.

**File edits require a game restart.** BepInEx 5's `ConfigFile` exposes `Reload()` but installs
no filesystem watcher, so editing the `.cfg` while the game is running has no effect until the
next launch. The in-game editor above is the way around that.

| Setting | Default | Notes |
|---|---|---|
| `Enabled` | `true` | Master switch |
| `OnDestroy` | `2.0` | Tree → logs, log → wood. **The one you want.** |
| `OnDamage` | `1.0` | Per-hit drops; often woodchips, so raising it can spam particles |
| `Respawner` | `1.0` | Timed regrowth; raising it stacks objects on one spawn point |
| `OnlyThesePrefabs` | *(empty)* | Comma-separated name fragments; empty means everything |
| `PrefabOverrides` | *(empty)* | `Name=Multiplier, ...` — beats the category multiplier |
| `MaxAmountPerEntry` | `200` | Hard cap; guards against a typo becoming a physics bomb |
| `ScatterRadius` | `0.35` | Metres of horizontal spread for stacked extras; `0` disables |
| `LogSpawns` | `false` | Logs every drop as `[Trigger] PrefabName: 2 -> 6 (x3)` |

### Stack size vs object count

`StackMultiplier` (section `2b`) multiplies what a single pickup is *worth* rather than spawning
more objects. One trunk worth 5 is free at runtime; five trunks are five rigidbodies landing on
one point. Prefer it over raising `OnDestroy`.

It is deliberately gated on `OnlyThesePrefabs` alone — **with an empty whitelist it does
nothing.** Stack size lands straight in your inventory, so a mis-scoped multiplier here is much
harder to notice than extra objects on the ground.

```ini
[2b. Stack size]
StackMultiplier = 5

[3. Targeting]
OnlyThesePrefabs = p_Trunk_
```

The boost is applied around `Collectable.TryToCollect` and undone in a Finalizer, so
`saveData.amount` is never written. That is not optional: the game itself does read-modify-write
on that value (`OnTrigger_SpawnShopOrders` runs `component.Amount *= order.count`), so a boost
left in place would compound into the save and grow every time.

## Chop Chop Tweaks

Config: `<game>/BepInEx/config/chopchopmods.tweaks.cfg`

| Setting | Default | Notes |
|---|---|---|
| `Axe.DamageMultiplier` | `1.0` | Fewer swings per tree. Only scales damage, never healing — `Health.ChangeHealth` does `CurrentHealth += delta`, so damage is the negative side |
| `Economy.IncomeMultiplier` | `1.0` | Money from selling |
| `Economy.CostMultiplier` | `1.0` | Money spent in the shop; `0` makes purchases free |
| `Movement.WalkSpeedMultiplier` | `1.0` | **Applies on restart** — set in `Move.Awake`, so live edits wait for the next save load |
| `Movement.RunSpeedMultiplier` | `1.0` | The game derives ground acceleration from run÷walk, so scale both together to keep the vanilla feel. **Applies on restart** |
| `Crafting.SkipMinigame` | `false` | Crafts instantly on recipe selection; ingredients still required and consumed |
| `Crafting.OutputMultiplier` | `1.0` | Multiplies craft yield via stack size, not object count — free at runtime. Ingredient cost is unchanged, so this is the recipe efficiency knob |
| `Crafting.MaxOutputPerItem` | `200` | Hard cap on a single output stack; guards against a mistyped multiplier |
| `Crafting.AutoCraftSpeedMultiplier` | `1.0` | Automated crafters only — hand crafting has no timer. Composes with the game's own CraftSpeed upgrades |
| `ItemMagnet.Radius` | `0.0` | Metres. `0` disables |
| `ItemMagnet.IntervalSeconds` | `0.2` | Scan cadence |
| `ItemMagnet.IncludeActiveCollectables` | `false` | Whether to also grab items meant for deliberate pickup |

Income and cost are separate multipliers because `MoneyServiceImpl.Change` is the only mutation
point and its *sign* is the only thing distinguishing a sale from a purchase — one multiplier
would make selling lucrative and buying free simultaneously.

The minigame skip hooks `MinigameCraftingServiceImpl.SetRecipe` and calls `Crafter.Craft(recipe)`
directly — the same public method `MinigameCraftingObject` calls when you solve the minigame, so
ingredient checks and consumption are unchanged.

### Crafting multipliers

Both scale an argument rather than game data, which is what keeps them safe.

**Output** patches `Crafter.SpawnRecipeItem`, the one method every craft output passes through, and
scales the `amount` it writes to `Collectable.Amount`. Recipes flagged `spawnAsSingleItem` call it
once with the full amount; the rest call it once per unit with `1`. Either way the object count is
untouched and only stack size grows — the same "multiply what a pickup is worth, not how many exist"
trade-off behind More Wood's `StackMultiplier`, and for the same reason: extra rigidbodies landing on
one spawn point is what `ScatterRadius` exists to mitigate.

Writing `recipe.output` instead would be a trap. `Recipe` is a ScriptableObject whose output array is
shared by every crafter using it, so the amounts would compound each craft and persist into the
asset. No undo is needed either — the boosted value lands on a freshly spawned `Collectable` rather
than being read back and rewritten, unlike the stack boost applied at pickup time in More Wood.

**Speed** patches `Crafter.Tick`, whose only use of `deltaTime` is
`CurrentCraftTimer += deltaTime * craftSpeedFactor`. Scaling the argument leaves `craftSpeedFactor`
alone deliberately: that field belongs to the game's upgrade system, which does
`craftSpeedFactor += delta` when a CraftSpeed upgrade is bought, so writing it would be overwritten
by the next upgrade. Scaling `deltaTime` composes with upgrades instead. Only automated crafters
have a timer — hand crafting goes straight through `CraftDefault` — and the game crafts at most once
per tick, so very large values stop helping.

The magnet is a behaviour, not a patch, because nothing in the game polls for nearby pickups. It
still collects through `Collectable.TryToCollect`, which runs the game's own player, inventory and
player-stat checks — so it cannot pick up anything you could not have collected by walking into it.

### Finding prefab names

Prefab names aren't visible without running the game. Set `LogSpawns = true`, chop a tree and
a log, then read `<game>/BepInEx/LogOutput.log`. Feed what you see into `OnlyThesePrefabs` or
`PrefabOverrides`:

```ini
PrefabOverrides = Log=4, Wood=6
```

## Layout

```
src/MoreWood/
  Plugin.cs            BepInEx entry point, config schema, startup self-check
  SpawnPatch.cs        the single prefix on SpawnOnDestroy.Spawn
  SpawnRewriter.cs     multiplier resolution, cloning, scatter, override parsing
  SpawnTrigger.cs      per-caller trigger tracking
  StackPatch.cs        whitelist-scoped stack size at pickup
src/ChopChopTweaks/
  Plugin.cs            config schema, magnet host, startup self-check
  AxeDamagePatch.cs    ChangeTargetHealth.Use
  MoneyPatch.cs        MoneyServiceImpl.Change
  MoveSpeedPatch.cs    Move.Awake
  MinigameSkipPatch.cs MinigameCraftingServiceImpl.SetRecipe
  CraftUIRestorer.cs   reopens the recipe list after a skipped craft
  CraftOutputPatch.cs  Crafter.SpawnRecipeItem
  CraftSpeedPatch.cs   Crafter.Tick
  ItemMagnet.cs        OverlapSphere -> Collectable.TryToCollect
scripts/            fetch-bepinex.sh, deploy.sh, decompile.sh
tools/              BepInEx redistributable (gitignored)
decompiled/         game C# for reference (gitignored)
```

`decompiled/` and `tools/` are gitignored — the former is derived from the game's copyrighted
assemblies, the latter is a third-party download. The build references game DLLs in place from
`CHOPCHOP_DIR` rather than vendoring them.

## Surviving game updates

The main patch target is referenced through a compile-time `SignatureGuard`, so if
`SpawnOnDestroy.Spawn`'s signature changes the **build breaks** instead of failing silently at
runtime. The three trigger patches target private methods by name and can't be compile-checked,
so `Plugin.VerifyPatchTargets()` checks them at startup and logs one actionable warning.

After a game update: re-run `./scripts/decompile.sh` and diff `WorldObjects/SpawnOnDestroy.cs`.

## Status

Built and deployed; **not yet verified in-game** — that needs a Windows launch of the Steam
build. First run to check: BepInEx console shows `More Wood loaded.`, then chop a tree with
`LogSpawns = true` and confirm the `->` amounts in `LogOutput.log`.
