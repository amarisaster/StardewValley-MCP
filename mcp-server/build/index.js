"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
const index_js_1 = require("@modelcontextprotocol/sdk/server/index.js");
const stdio_js_1 = require("@modelcontextprotocol/sdk/server/stdio.js");
const fs = __importStar(require("fs"));
const path = __importStar(require("path"));
const types_js_1 = require("@modelcontextprotocol/sdk/types.js");
// Point these to where the SMAPI mod is installed (its Mods/StardewMCPBridge/ folder)
const BRIDGE_PATH = process.env.STARDEW_BRIDGE_PATH
    || path.resolve(__dirname, "../../smapi-mod/bridge_data.json");
const ACTION_PATH = process.env.STARDEW_ACTION_PATH
    || path.resolve(__dirname, "../../smapi-mod/actions.json");
function sendAction(action) {
    // Atomic write: write to temp file then rename to prevent partial reads
    const tmpPath = ACTION_PATH + ".tmp";
    fs.writeFileSync(tmpPath, JSON.stringify(action));
    fs.renameSync(tmpPath, ACTION_PATH);
    return "Command sent.";
}
function readBridge() {
    if (fs.existsSync(BRIDGE_PATH)) {
        return fs.readFileSync(BRIDGE_PATH, "utf-8");
    }
    return '{"error": "Bridge file not found. Is the SMAPI mod running?"}';
}
// Helper to read companion state from bridge data
function getCompanionState(companionName) {
    const raw = readBridge();
    try {
        const data = JSON.parse(raw);
        if (data.companions) {
            const companion = data.companions.find((c) => c.name === companionName);
            if (companion)
                return JSON.stringify(companion, null, 2);
        }
        return `Companion "${companionName}" not found in bridge data.`;
    }
    catch {
        return raw;
    }
}
function getCompanionSurroundings(companionName) {
    const raw = readBridge();
    try {
        const data = JSON.parse(raw);
        if (data.companions) {
            const companion = data.companions.find((c) => c.name === companionName);
            if (companion?.surroundings)
                return JSON.stringify({
                    tile: companion.tile,
                    location: companion.location,
                    surroundings: companion.surroundings,
                }, null, 2);
            if (companion)
                return `Companion "${companionName}" has no surroundings data (is it in player mode?).`;
        }
        return `Companion "${companionName}" not found in bridge data.`;
    }
    catch {
        return raw;
    }
}
function getCompanionInventory(companionName) {
    const raw = readBridge();
    try {
        const data = JSON.parse(raw);
        if (data.companions) {
            const companion = data.companions.find((c) => c.name === companionName);
            if (companion?.inventory)
                return JSON.stringify(companion.inventory, null, 2);
            if (companion)
                return `Companion "${companionName}" has no inventory data (is it in player mode?).`;
        }
        return `Companion "${companionName}" not found in bridge data.`;
    }
    catch {
        return raw;
    }
}
const COMPANION_ENUM = ["Companion1", "Companion2"];
const MODE_ENUM = ["follow", "farm", "mine", "fish", "idle", "player"];
const TOOL_ENUM = ["pickaxe", "axe", "hoe", "watering_can", "sword"];
const DIRECTION_DESC = "0=up, 1=right, 2=down, 3=left";
class StardewBridgeServer {
    server;
    constructor() {
        this.server = new index_js_1.Server({
            name: "stardew-mcp-bridge",
            version: "0.3.0",
        }, {
            capabilities: {
                tools: {},
            },
        });
        this.setupHandlers();
    }
    setupHandlers() {
        this.server.setRequestHandler(types_js_1.ListToolsRequestSchema, async () => ({
            tools: [
                // ============================
                // GLOBAL TOOLS (existing)
                // ============================
                {
                    name: "stardew_get_state",
                    description: "Get current game state — time, weather, location, player stats, companion status, nearby NPCs.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_spawn",
                    description: "Spawn companions into the game world near the player.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_follow",
                    description: "Make all companions follow the player.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_stay",
                    description: "Make all companions stop and stay at their current position.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_farm",
                    description: "Enable auto-farm mode for all companions.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_mine",
                    description: "Enable combat/mining mode for all companions.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_fish",
                    description: "Enable fishing mode for all companions.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_water_all",
                    description: "Instantly water all unwatered crops in the current location.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_harvest_all",
                    description: "Instantly harvest all ready crops in the current location.",
                    inputSchema: { type: "object", properties: {} },
                },
                {
                    name: "stardew_chat",
                    description: "Send a chat message in the game.",
                    inputSchema: {
                        type: "object",
                        properties: { message: { type: "string" } },
                        required: ["message"],
                    },
                },
                {
                    name: "stardew_warp",
                    description: "Warp all companions to a location.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            location: { type: "string", description: "Location name (Farm, Town, Mine, Beach, Forest, Mountain, etc.)." },
                            x: { type: "number", description: "Tile X." },
                            y: { type: "number", description: "Tile Y." },
                        },
                        required: ["location", "x", "y"],
                    },
                },
                {
                    name: "stardew_set_mode",
                    description: "Set a specific companion's mode. Modes: follow, farm, mine, fish, idle, player.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            target: { type: "string", enum: COMPANION_ENUM },
                            mode: { type: "string", enum: MODE_ENUM },
                        },
                        required: ["target", "mode"],
                    },
                },
                {
                    name: "stardew_action",
                    description: "Send a custom action (water, harvest, clear, hoe) at a tile.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            actionType: { type: "string" },
                            x: { type: "number" },
                            y: { type: "number" },
                        },
                        required: ["actionType"],
                    },
                },
                // ============================
                // PLAYER MODE — Direct Control
                // ============================
                {
                    name: "stardew_get_surroundings",
                    description: "Get what a companion can see — tiles, objects, crops, monsters, NPCs in a radius around them. The companion's 'eyes'. Companion must be in player mode.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM, description: "Which companion." },
                        },
                        required: ["companion"],
                    },
                },
                {
                    name: "stardew_get_inventory",
                    description: "Get a companion's inventory — tools and items they're carrying.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                        },
                        required: ["companion"],
                    },
                },
                {
                    name: "stardew_get_companion_state",
                    description: "Get detailed state for a specific companion — position, location, health, stamina, mode, surroundings (if in player mode), inventory, last command result.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                        },
                        required: ["companion"],
                    },
                },
                {
                    name: "stardew_move_to",
                    description: "Move a companion to a tile via pathfinding. Async — check surroundings on next tick to see progress.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                            x: { type: "number", description: "Target tile X." },
                            y: { type: "number", description: "Target tile Y." },
                        },
                        required: ["companion", "x", "y"],
                    },
                },
                {
                    name: "stardew_warp_companion",
                    description: "Teleport a specific companion to a named location.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                            location: { type: "string", description: "Location name." },
                            x: { type: "number", description: "Tile X." },
                            y: { type: "number", description: "Tile Y." },
                        },
                        required: ["companion", "location", "x", "y"],
                    },
                },
                {
                    name: "stardew_face_direction",
                    description: `Turn a companion to face a direction. ${DIRECTION_DESC}.`,
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                            direction: { type: "number", description: DIRECTION_DESC },
                        },
                        required: ["companion", "direction"],
                    },
                },
                {
                    name: "stardew_use_tool",
                    description: "Use a tool at a tile. Tools: pickaxe, axe, hoe, watering_can, sword.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                            tool: { type: "string", enum: TOOL_ENUM },
                            x: { type: "number", description: "Target tile X." },
                            y: { type: "number", description: "Target tile Y." },
                        },
                        required: ["companion", "tool", "x", "y"],
                    },
                },
                {
                    name: "stardew_interact",
                    description: "Interact with an object, crop, chest, ladder, or machine at a tile.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                            x: { type: "number", description: "Target tile X." },
                            y: { type: "number", description: "Target tile Y." },
                        },
                        required: ["companion", "x", "y"],
                    },
                },
                {
                    name: "stardew_attack",
                    description: "Attack the nearest monster with equipped weapon.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                        },
                        required: ["companion"],
                    },
                },
                {
                    name: "stardew_cast_fishing_rod",
                    description: "Cast the fishing rod and auto-hook when a fish bites. Companion must be near water.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                        },
                        required: ["companion"],
                    },
                },
                {
                    name: "stardew_set_auto_combat",
                    description: "Toggle auto-combat: when enabled, the companion automatically attacks nearby monsters each tick.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                            enabled: { type: "boolean", description: "true to enable, false to disable." },
                        },
                        required: ["companion", "enabled"],
                    },
                },
                {
                    name: "stardew_eat_item",
                    description: "Eat a food item from inventory to restore health/stamina.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            companion: { type: "string", enum: COMPANION_ENUM },
                            slot: { type: "number", description: "Inventory slot index (optional — picks first edible if omitted)." },
                        },
                        required: ["companion"],
                    },
                },
            ],
        }));
        this.server.setRequestHandler(types_js_1.CallToolRequestSchema, async (request) => {
            const { name, arguments: args } = request.params;
            const a = (args || {});
            try {
                // --- Global tools ---
                switch (name) {
                    case "stardew_get_state":
                        return ok(readBridge());
                    case "stardew_spawn":
                        return ok(sendAction({ actionType: "spawn" }));
                    case "stardew_follow":
                        return ok(sendAction({ actionType: "follow" }));
                    case "stardew_stay":
                        return ok(sendAction({ actionType: "stay" }));
                    case "stardew_farm":
                        return ok(sendAction({ actionType: "farm" }));
                    case "stardew_water_all":
                        return ok(sendAction({ actionType: "water_all" }));
                    case "stardew_harvest_all":
                        return ok(sendAction({ actionType: "harvest_all" }));
                    case "stardew_mine":
                        return ok(sendAction({ actionType: "mine" }));
                    case "stardew_fish":
                        return ok(sendAction({ actionType: "fish" }));
                    case "stardew_warp":
                        if (!a.location || a.x == null || a.y == null)
                            return err("location, x, and y are required.");
                        return ok(sendAction({ actionType: "warp", location: a.location, x: a.x, y: a.y }));
                    case "stardew_set_mode":
                        if (!a.target || !a.mode)
                            return err("target and mode are required.");
                        return ok(sendAction({ actionType: "set_mode", target: a.target, mode: a.mode }));
                    case "stardew_chat":
                        if (!a.message)
                            return err("message is required.");
                        return ok(sendAction({ actionType: "chat", metadata: { message: a.message } }));
                    case "stardew_action":
                        if (!a.actionType)
                            return err("actionType is required.");
                        return ok(sendAction(a));
                    // --- Player mode: companion-targeted commands ---
                    case "stardew_get_surroundings":
                        if (!a.companion)
                            return err("companion is required.");
                        // Extract just surroundings from bridge data (companion must be in player mode)
                        return ok(getCompanionSurroundings(a.companion));
                    case "stardew_get_inventory":
                        if (!a.companion)
                            return err("companion is required.");
                        return ok(getCompanionInventory(a.companion));
                    case "stardew_get_companion_state":
                        if (!a.companion)
                            return err("companion is required.");
                        return ok(getCompanionState(a.companion));
                    case "stardew_move_to":
                        if (!a.companion || a.x == null || a.y == null)
                            return err("companion, x, and y are required.");
                        return ok(sendAction({
                            actionType: "move_to",
                            companion: a.companion,
                            x: a.x, y: a.y,
                        }));
                    case "stardew_warp_companion":
                        if (!a.companion || !a.location || a.x == null || a.y == null)
                            return err("companion, location, x, and y are required.");
                        return ok(sendAction({
                            actionType: "warp_to",
                            companion: a.companion,
                            location: a.location,
                            x: a.x, y: a.y,
                        }));
                    case "stardew_face_direction":
                        if (!a.companion || a.direction == null)
                            return err("companion and direction are required.");
                        return ok(sendAction({
                            actionType: "face_direction",
                            companion: a.companion,
                            direction: a.direction,
                        }));
                    case "stardew_use_tool":
                        if (!a.companion || !a.tool || a.x == null || a.y == null)
                            return err("companion, tool, x, and y are required.");
                        return ok(sendAction({
                            actionType: "use_tool",
                            companion: a.companion,
                            tool: a.tool,
                            x: a.x, y: a.y,
                        }));
                    case "stardew_interact":
                        if (!a.companion || a.x == null || a.y == null)
                            return err("companion, x, and y are required.");
                        return ok(sendAction({
                            actionType: "interact",
                            companion: a.companion,
                            x: a.x, y: a.y,
                        }));
                    case "stardew_attack":
                        if (!a.companion)
                            return err("companion is required.");
                        return ok(sendAction({
                            actionType: "attack",
                            companion: a.companion,
                        }));
                    case "stardew_cast_fishing_rod":
                        if (!a.companion)
                            return err("companion is required.");
                        return ok(sendAction({
                            actionType: "cast_fishing_rod",
                            companion: a.companion,
                        }));
                    case "stardew_set_auto_combat":
                        if (!a.companion || a.enabled == null)
                            return err("companion and enabled are required.");
                        return ok(sendAction({
                            actionType: "set_auto_combat",
                            companion: a.companion,
                            enabled: a.enabled,
                        }));
                    case "stardew_eat_item":
                        if (!a.companion)
                            return err("companion is required.");
                        return ok(sendAction({
                            actionType: "eat_item",
                            companion: a.companion,
                            ...(a.slot != null ? { slot: a.slot } : {}),
                        }));
                    default:
                        return err(`Unknown tool: ${name}`);
                }
            }
            catch (error) {
                return err(error.message);
            }
        });
    }
    async run() {
        const transport = new stdio_js_1.StdioServerTransport();
        await this.server.connect(transport);
        console.error("Stardew MCP Bridge v0.3.0 running on stdio");
    }
}
function ok(text) {
    return { content: [{ type: "text", text }] };
}
function err(text) {
    return { content: [{ type: "text", text: `Error: ${text}` }] };
}
const server = new StardewBridgeServer();
server.run().catch(console.error);
