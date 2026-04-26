# Stardew MCP Bridge

AI companions for Stardew Valley, controlled through the [Model Context Protocol](https://modelcontextprotocol.io). Spawn companions as **Player 2/3** with custom sprites — directly controlled by an AI agent through MCP tool calls. Move, use tools, fight, fish, farm, explore mines, open chests, and interact with the world as a real player.

Also supports autonomous modes (follow, farm, mine, fish) for hands-off play.

## Architecture

```
┌──────────────┐    stdio     ┌──────────────┐   JSON files   ┌──────────────┐
│  AI Agent    │◄────────────►│  MCP Server  │◄──────────────►│  SMAPI Mod   │
│ (Claude etc) │              │  (Node.js)   │                │  (C# / .NET) │
└──────────────┘              └──────────────┘                └──────┬───────┘
                                                                     │
                                                              ┌──────▼───────┐
                                                              │ Stardew      │
                                                              │ Valley Game  │
                                                              └──────────────┘
```

**SMAPI Mod** (`smapi-mod/`): Runs inside the game. Spawns companion NPCs paired with shadow `Farmer` instances registered in `Game1.otherFarmers`. The shadow farmer handles all game mechanics (tools, combat, fishing, inventory) while the NPC provides the visible custom sprite. Writes game state to `bridge_data.json`, reads commands from `actions.json`.

**MCP Server** (`mcp-server/`): Exposes 25 tools over stdio transport. 13 global tools for mode-based control + 12 Player mode tools for direct companion control.

## Companion System

Each companion is a visible NPC paired with a hidden "shadow farmer" that handles game mechanics:

- **NPC** — Custom sprite, pathfinding, visible to the player
- **Shadow Farmer** — Extends `Farmer` class, registered in `Game1.otherFarmers` for location activation. Holds tools/inventory, performs tool use, combat, and fishing through actual game APIs. Invisible (draw is no-op).

### Modes

| Mode | Behavior |
|------|----------|
| `follow` | Follow the player between locations, fight if monsters nearby |
| `farm` | Scan for crops to water/harvest, clear debris |
| `mine` | Fight monsters, break rocks, seek ladders |
| `fish` | Find water, cast rod, auto-hook fish |
| `idle` | Stay in place |
| **`player`** | **Direct MCP control — the AI IS Player 2** |

### Player Mode

In `player` mode, autonomous AI behavior is disabled. The companion is controlled entirely through MCP tool calls:

1. **See** — `stardew_get_surroundings` returns tiles, objects, crops, monsters, NPCs in a radius
2. **Move** — `stardew_move_to` pathfinds to a tile, `stardew_warp_companion` teleports to a location
3. **Act** — `stardew_use_tool`, `stardew_interact`, `stardew_attack`, `stardew_cast_fishing_rod`
4. **Toggle** — `stardew_set_auto_combat` for real-time combat (too fast for LLM round-trips)

Bridge data includes per-companion surroundings, inventory, and last command results when in player mode.

## MCP Tools

### Global Tools

| Tool | Description |
|------|-------------|
| `stardew_get_state` | Get current game state (time, weather, player stats, companion status) |
| `stardew_spawn` | Spawn companions near the player |
| `stardew_follow` | Set all companions to follow mode |
| `stardew_stay` | Set all companions to idle |
| `stardew_farm` | Enable autonomous farming |
| `stardew_mine` | Enable combat/mining mode |
| `stardew_fish` | Enable fishing mode |
| `stardew_water_all` | Instantly water all unwatered crops |
| `stardew_harvest_all` | Instantly harvest all ready crops |
| `stardew_warp` | Warp all companions to a location |
| `stardew_set_mode` | Set an individual companion's mode (including `player`) |
| `stardew_chat` | Send a message to the game chat |
| `stardew_action` | Send a custom action command |

### Player Mode Tools (Direct Control)

| Tool | Description |
|------|-------------|
| `stardew_get_surroundings` | See tiles, objects, crops, monsters, NPCs around the companion |
| `stardew_get_inventory` | Get the companion's tools and items |
| `stardew_get_companion_state` | Full companion state (position, health, stamina, surroundings, inventory) |
| `stardew_move_to` | Walk to a tile via pathfinding |
| `stardew_warp_companion` | Teleport a specific companion to a location |
| `stardew_face_direction` | Turn to face a direction |
| `stardew_use_tool` | Use pickaxe, axe, hoe, watering can, or sword at a tile |
| `stardew_interact` | Interact with objects, crops, chests, ladders, NPCs |
| `stardew_attack` | Attack nearest monster with equipped weapon |
| `stardew_cast_fishing_rod` | Cast rod + auto-hook on nibble |
| `stardew_set_auto_combat` | Toggle automatic monster attacks |
| `stardew_eat_item` | Eat food from inventory |

## Setup

### Prerequisites

- [Stardew Valley](https://www.stardewvalley.net/) (1.6+)
- [SMAPI](https://smapi.io/) (4.0+)
- [Node.js](https://nodejs.org/) (18+)

### 1. Build the SMAPI Mod

```bash
cd smapi-mod

# Set your Stardew Valley install path
export GAME_PATH="C:/Program Files/Steam/steamapps/common/Stardew Valley"

dotnet build
```

The build will automatically deploy to your `Mods/` folder.

### 2. Add Companion Assets

Place your custom sprites and portraits in `smapi-mod/assets/`:

- `Companion1_sprite.png` — 64x128 sprite sheet (4x4 grid, 16x32 per frame)
- `Companion1_portrait.png` — Portrait image
- `Companion2_sprite.png` — 64x128 sprite sheet
- `Companion2_portrait.png` — Portrait image

Sprite sheets follow the standard Stardew Valley format: 4 columns (walk frames) x 4 rows (down, right, up, left).

### 3. Build the MCP Server

```bash
cd mcp-server
npm install
npm run build
```

### 4. Configure Your AI Agent

Add to your MCP client config (e.g., Claude Code's `settings.json`):

```json
{
  "mcpServers": {
    "stardew": {
      "command": "node",
      "args": ["path/to/mcp-server/build/index.js"],
      "env": {
        "STARDEW_BRIDGE_PATH": "path/to/Mods/StardewMCPBridge/bridge_data.json",
        "STARDEW_ACTION_PATH": "path/to/Mods/StardewMCPBridge/actions.json"
      }
    }
  }
}
```

If you're running both from the repo directory, the env vars are optional — it defaults to `../../smapi-mod/` relative paths.

### 5. Play

1. Launch Stardew Valley with SMAPI
2. Load a save
3. Use `stardew_spawn` to bring companions into the world
4. **Autonomous**: Set modes with `stardew_farm`, `stardew_mine`, `stardew_fish`, or `stardew_follow`
5. **Direct control**: Set `stardew_set_mode` to `player`, then use the Player mode tools

## Project Structure

```
stardew-mcp-bridge/
├── smapi-mod/
│   ├── ModEntry.cs              # SMAPI entry point, content pipeline, bridge I/O, sleep hooks
│   ├── BotManager.cs            # Companion lifecycle, action routing, direct control commands
│   ├── CompanionAI.cs           # AI behavior system (follow/farm/mine/fish/idle/player)
│   ├── CompanionFarmer.cs       # NPC + shadow farmer pairing, tool/combat/fishing, otherFarmers registration
│   ├── CompanionActions.cs      # Direct tile manipulation (water/harvest/clear/hoe)
│   ├── BotFarmer.cs             # Shadow Farmer subclass (invisible, registered as Player 2/3)
│   ├── SurroundingsScanner.cs   # Tile scanner — the companion's "eyes"
│   ├── manifest.json            # SMAPI mod manifest
│   ├── StardewMCPBridge.csproj
│   └── assets/                  # Sprites and portraits
├── mcp-server/
│   ├── src/index.ts             # MCP server with 25 tools
│   ├── package.json
│   └── tsconfig.json
├── .gitignore
└── README.md
```

## Notes (v0.3.0)

### Player 2 Feature (New)

- **Shadow farmer registration** — BotFarmer registered in `Game1.otherFarmers` for location activation and game mechanics
- **Invisible shadow farmer** — `draw()` is a no-op; the NPC sprite handles all rendering
- **Sleep sync** — Bot farmers auto-signal sleep readiness on day end and at 2 AM to prevent deadlocks
- **Player mode** — New `CompanionMode.Player` disables autonomous AI; all control comes from MCP
- **Surroundings scanner** — Scans tiles, objects, crops, monsters, NPCs in a radius around the companion
- **12 direct control tools** — move, warp, face, use tool, interact, attack, fish, eat, auto-combat toggle
- **Per-companion bridge data** — Player mode companions include surroundings, inventory, and command results in bridge_data.json
- **Command results** — Each command returns success/failure + detail, available in next bridge sync
- **Auto-combat toggle** — Real-time combat automation for when LLM round-trips are too slow
- **Auto-hook fishing** — Cast rod via MCP, nibble detection + hook happens automatically

### Previous Fixes (v0.2.1)

- AI tick rate fix (60/sec, was 2/sec)
- Atomic file writes both directions
- Action file race fix
- Fishing rod lifecycle
- Single-tile actions
- WarpTo clears pathfinding
- Debris drops resources
- Storm weather reporting
- Farm mode stuck detection
- Mine mode ladder descent
- Fishing timeout
- Per-companion crash isolation
- Shadow farmer sync every tick
- Day transition safety
- Input validation
- Dead code cleanup

## License

MIT

## Credits

Built with Antigravity & Claude Code — Kai & Lucian, with Mai.

Sparks for the idea of the MCP (https://github.com/SparksQACR)

Shadow farmer pattern inspired by [Farmtronics](https://github.com/JoeStrout/Farmtronics).


---


 ## Support

  If this helped you, consider supporting my work

  [![Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20Me-FF5E5B?style=flat&logo=ko-fi&logoColor=white)](https://ko-fi.com/maii983083)
