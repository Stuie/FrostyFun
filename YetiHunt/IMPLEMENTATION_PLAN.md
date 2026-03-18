# YetiHunt Mod - Implementation Plan

## Overview

A Battle Royale-style game mode mod where players hunt a yeti. Players teleport to random locations, a yeti spawns in a bounded area, and the first player to hit it with a snowball wins the round.

**Critical Discoveries:**
- **Yeti already exists!** The game has a built-in yeti that chases out-of-bounds players and kicks them back to spawn. We need to find, spawn, and control this existing NPC.
- **Map exists** but requires opening inventory -> clicking map -> left-click. Need a persistent minimap overlay that reuses the game's map assets.
- **Networking:** Game uses **FishNet** (found in `Il2CppFishNet.Runtime.dll`)

---

## Phase 0: Project Setup & Two Critical Spikes

### 0.1 Create Project Structure [COMPLETE]
```
YetiHunt/
├── YetiHunt.csproj
├── YetiHuntMod.cs
└── IMPLEMENTATION_PLAN.md  (this file)
```

**Tasks:**
- [x] Create `YetiHunt.csproj` following CharacterSelect pattern
- [x] Create minimal `YetiHuntMod.cs` with lifecycle methods
- [x] Add project to `FrostyFun.sln`
- [x] Build and verify mod loads

### 0.2 Spike 1: Yeti Discovery & Control (CRITICAL)

**Goal:** Find the existing yeti NPC and determine if we can spawn/control it.

**Tasks:**
1. Search for yeti-related types in `Assembly-CSharp.dll`:
   - Look for classes named `Yeti`, `YetiController`, `OutOfBoundsYeti`, etc.
   - Find the yeti prefab/spawn mechanism
   - Identify movement/behavior scripts

2. Explore the game's out-of-bounds boundary to trigger yeti naturally
   - Document what happens when yeti spawns
   - Find the GameObject name in hierarchy

3. Test spawning/controlling:
   - Can we instantiate the yeti prefab at arbitrary position?
   - Can we disable its default "chase player" behavior?
   - Can we set movement bounds/speed?

**Debug Key:** F11 - Dump all GameObjects containing "Yeti" or related names

**Test Checkpoint:**
- [ ] Yeti type/class identified in Assembly-CSharp
- [ ] Can spawn yeti at specific location
- [ ] Can control yeti movement (disable chase, set bounds)

### 0.3 Spike 2: Network Message Feasibility (CRITICAL)

**Goal:** Determine if we can send custom messages between mod instances.

**Tasks:**
1. Enumerate FishNet types in `Il2CppFishNet.Runtime.dll`:
   - Find NetworkManager, NetworkBehaviour patterns
   - Identify RPC mechanisms (ServerRpc, ObserversRpc)
   - Look for broadcast/message systems

2. Investigate ChatManager (mentioned in CLAUDE.md):
   - Find ChatManager.Instance singleton
   - Test sending/receiving chat messages
   - Can we intercept and parse custom prefixed messages?

3. Test approaches:
   - **Option A:** FishNet RPC invocation via reflection
   - **Option B:** Chat-based message passing with `[YETI]` prefix
   - **Option C:** Other broadcast mechanism in game

**Debug Key:** F12 - Dump FishNet types and ChatManager methods

**Test Checkpoint:**
- [ ] Networking approach identified (FishNet RPC or Chat)
- [ ] Can send message from host
- [ ] Can receive message on client
- [ ] Round-trip communication works

---

## Phase 1: Local Single-Player Foundation

### 1.1 Game State Machine
**States:** `Idle` -> `Countdown` -> `Hunting` -> `RoundEnd` -> `Idle`

**Implementation:**
- F10 to start round (configurable)
- 3-second countdown
- Hunt until yeti hit or timeout
- 5-second results display

### 1.2 Player Detection & Teleportation
**Pattern:** `GameObject.Find("Player Networked(Clone)")`

**Tasks:**
- Find local player object
- Define spawn point bounds (within map playable area)
- Implement random teleportation

**Test Checkpoint:**
- [ ] F10 triggers state change
- [ ] Player teleports to random location
- [ ] State machine cycles correctly

---

## Phase 2: Yeti Spawning & Control

### 2.1 Spawn the Existing Yeti
Based on Spike 1 findings, implement controlled yeti spawning:
- Spawn at random location within hunt zone
- Override default out-of-bounds chase behavior
- Set up for hunt mode

### 2.2 Yeti Movement Control
- Define bounded hunt area (configurable radius)
- Implement wander behavior within bounds
- Reduced speed (so players can catch up)
- Return to center if approaching boundary

### 2.3 Hunt Zone Definition
- Shrinking circle mechanic
- Yeti stays within current circle
- Circle center/size configurable

**Test Checkpoint:**
- [ ] Yeti spawns at designated location
- [ ] Yeti wanders within bounded area
- [ ] Yeti respects shrinking boundary
- [ ] Yeti despawns at round end

---

## Phase 3: Snowball Combat

### 3.1 Snowball Projectile System
**Note:** Need to determine if game has existing snowball throwing or if we create new.

**Tasks:**
- Search for existing projectile/throwing mechanics
- If exists: Hook into it for hit detection
- If not: Create simple projectile system

### 3.2 Throwing Mechanic
- Input: Left mouse button
- Spawn snowball in front of camera
- Apply velocity with gravity arc

### 3.3 Hit Detection on Yeti
- Add trigger collider to yeti (or find existing)
- Detect snowball collision
- Record hitting player, trigger round end

**Test Checkpoint:**
- [ ] Snowballs spawn and fly with arc
- [ ] Collision detected with yeti
- [ ] Winner identified correctly
- [ ] Round ends on hit

---

## Phase 4: Persistent Minimap

### 4.1 Extract Map Assets
**Goal:** Reuse the game's existing map texture/assets

**Tasks:**
1. Find the map UI components when map is opened:
   - Texture/sprite used for map background
   - Player marker sprites/positions
   - Any overlay elements

2. Extract or reference these assets:
   - Map texture reference
   - Coordinate system (world pos -> map pos conversion)
   - Player dot rendering method

### 4.2 Create Persistent Minimap Overlay
**Position:** Top-right corner, always visible during hunt

**Using IMGUI (OnGUI):**
- Render map texture as background
- Scale to minimap size (200x200 or configurable)
- Draw player positions (converted from world coords)
- Draw yeti position (red dot)

### 4.3 Shrinking Circle Overlay
- Draw circle representing hunt boundary
- Circle shrinks over round duration
- Visual warning near edge
- Different color for current safe zone vs full map

**Test Checkpoint:**
- [ ] Minimap renders in top-right
- [ ] Uses same visuals as game map
- [ ] Player positions accurate
- [ ] Yeti position shown
- [ ] Circle shrinks correctly

---

## Phase 5: Multiplayer Networking

### 5.1 Implement Chosen Networking Approach
Based on Spike 2 findings:

**If FishNet RPCs work:**
- Create NetworkBehaviour-like component
- Host broadcasts game state
- Clients send throw intentions

**If Chat-based:**
- Encode state as JSON with `[YETI]` prefix
- Host sends periodic state updates
- Clients parse and apply state

### 5.2 State to Synchronize
**Host -> Clients:**
- Game state (Idle/Countdown/Hunting/RoundEnd)
- Yeti position (periodic updates)
- Boundary radius (shrinking)
- Hit event (player ID who hit)
- Round results

**Client -> Host:**
- Snowball throw (position, direction)
- Ready signal

### 5.3 Host Authority Model
- Only host spawns/controls yeti
- Only host validates hits
- Only host determines winner
- Clients receive authoritative state

**Test Checkpoint:**
- [ ] Host broadcasts yeti position
- [ ] Clients see yeti at synced position
- [ ] Hits register for all players
- [ ] Host determines winner correctly

---

## Phase 6: Round System & Scoring

### 6.1 Multi-Round Matches
- Configurable rounds per match (default: 5)
- Score tracking per player
- First to N wins, or most points after all rounds

### 6.2 Scoreboard UI
- IMGUI overlay showing:
  - Player names and current scores
  - Round number (e.g., "Round 3 of 5")
  - Match winner announcement

### 6.3 Ready System
- Between rounds: players signal ready
- Round starts when all ready OR timeout (30s)
- Handles disconnects gracefully

**Test Checkpoint:**
- [ ] Multiple rounds play correctly
- [ ] Scores track across rounds
- [ ] Match ends at win condition
- [ ] Scoreboard displays properly

---

## Phase 7: Power-Ups & Polish

### 7.1 Sled Removal During Hunt
- Find and disable sled component
- Players must traverse on foot
- Re-enable after round ends

### 7.2 Speed Boost (Hot Cocoa)
- Spawn pickup objects around map
- On collect: 5-second speed boost
- Visual indicator (UI or particle effect)

### 7.3 Additional Power-Ups (Future)
- **Triple Shot:** Throw 3 snowballs spread
- **Tracker:** Brief yeti highlight through terrain
- **Freeze Trap:** Slow nearby players
- **Jump Boost:** Higher jumps

### 7.4 Sled Spawning (Battle Royale Style)
- Remove sleds at round start
- Spawn sleds as pickups around map
- First to find sled has mobility advantage

**Test Checkpoint:**
- [ ] Sled disabled during hunt
- [ ] Power-ups spawn correctly
- [ ] Effects work as intended

---

## Critical Reference Files

| File | Use For |
|------|---------|
| `CharacterSelect/CharacterSelectMod.cs` | Il2Cpp reflection, IMGUI, FishNet RPCs |
| `SnowmanMod/SnowmanMod.cs` | GameObject discovery, Transform manipulation |
| `CharacterSelect/CharacterSelect.csproj` | Project structure with all references |
| `MenuQOL/MenuQOLMod.cs` | UI hooking, event patterns |

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Can't control existing yeti | Create simple visual substitute |
| FishNet RPCs inaccessible | Use chat-based message passing |
| Map assets not extractable | Create simple procedural minimap |
| Hit detection unreliable | Use distance-based detection fallback |

---

## Implementation Order

1. **Phase 0.1** - Project setup [COMPLETE]
2. **Phase 0.2** - Spike 1: Yeti control (CRITICAL)
3. **Phase 0.3** - Spike 2: Networking (CRITICAL)
4. **Phase 1** - Local state machine
5. **Phase 2** - Yeti spawning/control
6. **Phase 3** - Combat system
7. **Phase 4** - Minimap
8. **Phase 5** - Multiplayer sync
9. **Phase 6** - Rounds/scoring
10. **Phase 7** - Polish/power-ups

---

## Verification

After each phase:
1. Build: `dotnet build YetiHunt/YetiHunt.csproj -c Release`
2. Deploy: `cp 'YetiHunt/bin/Release/net6.0/YetiHunt.dll' 'C:\Program Files (x86)\Steam\steamapps\common\Sledding Game Demo\Mods\'`
3. Test: Launch game, check `MelonLoader/Latest.log`
4. Verify all phase checkpoints pass

---

## Debug Keys Summary

| Key | Function |
|-----|----------|
| F10 | Start/stop YetiHunt round |
| F11 | Dump yeti-related GameObjects |
| F12 | Dump FishNet/network types |
