import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import * as fs from "fs";
import * as path from "path";
import {
    CallToolRequestSchema,
    ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

// Point these to where the SMAPI mod is installed (its Mods/StardewMCPBridge/ folder)
const BRIDGE_PATH = process.env.STARDEW_BRIDGE_PATH
    || path.resolve(__dirname, "../../smapi-mod/bridge_data.json");
const ACTION_PATH = process.env.STARDEW_ACTION_PATH
    || path.resolve(__dirname, "../../smapi-mod/actions.json");

function sendAction(action: object): string {
    fs.writeFileSync(ACTION_PATH, JSON.stringify(action));
    return "Command sent.";
}

function readBridge(): string {
    if (fs.existsSync(BRIDGE_PATH)) {
        return fs.readFileSync(BRIDGE_PATH, "utf-8");
    }
    return '{"error": "Bridge file not found. Is the SMAPI mod running?"}';
}

class StardewBridgeServer {
    private server: Server;

    constructor() {
        this.server = new Server(
            {
                name: "stardew-mcp-bridge",
                version: "0.2.0",
            },
            {
                capabilities: {
                    tools: {},
                },
            }
        );

        this.setupHandlers();
    }

    private setupHandlers() {
        this.server.setRequestHandler(ListToolsRequestSchema, async () => ({
            tools: [
                {
                    name: "stardew_get_state",
                    description: "Get current game state — time, weather, location, player stats, companion status, nearby NPCs.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_spawn",
                    description: "Spawn companions into the game world near the player. They will automatically follow her.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_follow",
                    description: "Make companions follow the player around. They will warp between locations with her.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_stay",
                    description: "Make companions stop and stay at their current position.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_farm",
                    description: "Enable auto-farm mode. companions will autonomously scan for farm tasks (watering, harvesting, clearing debris) and do them.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_water_all",
                    description: "Instantly water all unwatered crops in the current location.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_harvest_all",
                    description: "Instantly harvest all ready crops in the current location.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_chat",
                    description: "Send a chat message that appears in the game's chat box.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            message: { type: "string", description: "The message to display in game." },
                        },
                        required: ["message"],
                    },
                },
                {
                    name: "stardew_mine",
                    description: "Send companions into combat/mining mode. They will fight monsters, break rocks, and explore autonomously.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_fish",
                    description: "Send companions into fishing mode. They will find water and fish autonomously.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                    },
                },
                {
                    name: "stardew_warp",
                    description: "Warp companions to a specific game location.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            location: { type: "string", description: "Location name (Farm, Town, Mine, Beach, Forest, Mountain, etc.)." },
                            x: { type: "number", description: "Tile X coordinate at destination." },
                            y: { type: "number", description: "Tile Y coordinate at destination." },
                        },
                        required: ["location", "x", "y"],
                    },
                },
                {
                    name: "stardew_set_mode",
                    description: "Set a specific companion's mode independently. Modes: follow, farm, mine, fish, idle.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            target: { type: "string", enum: ["Companion1", "Companion2"], description: "Which companion." },
                            mode: { type: "string", enum: ["follow", "farm", "mine", "fish", "idle"], description: "The mode to set." },
                        },
                        required: ["target", "mode"],
                    },
                },
                {
                    name: "stardew_action",
                    description: "Send a custom action command to the game. For advanced use.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            actionType: { type: "string", description: "Action type (water, harvest, clear, hoe)." },
                            x: { type: "number", description: "Tile X coordinate." },
                            y: { type: "number", description: "Tile Y coordinate." },
                        },
                        required: ["actionType"],
                    },
                },
            ],
        }));

        this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
            const { name, arguments: args } = request.params;

            try {
                switch (name) {
                    case "stardew_get_state":
                        return { content: [{ type: "text", text: readBridge() }] };

                    case "stardew_spawn":
                        return { content: [{ type: "text", text: sendAction({ actionType: "spawn" }) }] };

                    case "stardew_follow":
                        return { content: [{ type: "text", text: sendAction({ actionType: "follow" }) }] };

                    case "stardew_stay":
                        return { content: [{ type: "text", text: sendAction({ actionType: "stay" }) }] };

                    case "stardew_farm":
                        return { content: [{ type: "text", text: sendAction({ actionType: "farm" }) }] };

                    case "stardew_water_all":
                        return { content: [{ type: "text", text: sendAction({ actionType: "water_all" }) }] };

                    case "stardew_harvest_all":
                        return { content: [{ type: "text", text: sendAction({ actionType: "harvest_all" }) }] };

                    case "stardew_mine":
                        return { content: [{ type: "text", text: sendAction({ actionType: "mine" }) }] };

                    case "stardew_fish":
                        return { content: [{ type: "text", text: sendAction({ actionType: "fish" }) }] };

                    case "stardew_warp":
                        return { content: [{ type: "text", text: sendAction({ actionType: "warp", ...(args as object) }) }] };

                    case "stardew_set_mode":
                        return { content: [{ type: "text", text: sendAction({ actionType: "set_mode", ...(args as object) }) }] };

                    case "stardew_chat":
                        return {
                            content: [{
                                type: "text",
                                text: sendAction({
                                    actionType: "chat",
                                    metadata: { message: (args as any)?.message || "" }
                                })
                            }]
                        };

                    case "stardew_action":
                        return { content: [{ type: "text", text: sendAction(args as object) }] };

                    default:
                        return { content: [{ type: "text", text: `Unknown tool: ${name}` }] };
                }
            } catch (error: any) {
                return { content: [{ type: "text", text: `Error: ${error.message}` }] };
            }
        });
    }

    async run() {
        const transport = new StdioServerTransport();
        await this.server.connect(transport);
        console.error("Stardew MCP Bridge v0.2.0 running on stdio");
    }
}

const server = new StardewBridgeServer();
server.run().catch(console.error);
