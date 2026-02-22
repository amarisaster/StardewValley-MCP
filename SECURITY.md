# Security & Privacy

Stardew MCP Bridge is a local mod that runs entirely on your machine. This document explains how it works and what to be aware of.

---

## How It Works

| Component | Runs On | Communicates Via |
|-----------|---------|------------------|
| **SMAPI Mod** | Inside Stardew Valley (local) | JSON files on disk |
| **MCP Server** | Local Node.js process | stdio (stdin/stdout) |

There are no network calls, no cloud services, no external APIs. The mod writes game state to a local JSON file, and the MCP server reads/writes local JSON files to send commands. Everything stays on your machine.

---

## What This Mod Can Do

| Category | Capabilities |
|----------|--------------|
| **Read** | Game state (time, weather, player stats, NPC positions) |
| **Write** | Spawn companions, set modes, send chat messages, warp locations |
| **Game Actions** | Water crops, harvest, clear debris (through game APIs) |

> **What this means:** The mod interacts with your single-player game session. It cannot access your files, network, or anything outside the game.

---

## File Access

The mod reads and writes two files in its own mod folder:

| File | Purpose |
|------|---------|
| `bridge_data.json` | Game state snapshot (read by MCP server) |
| `actions.json` | Command queue (written by MCP server, read by mod) |

These files contain only game data (time of day, player position, crop states, etc). No personal information is stored.

---

## Best Practices

- **Review the code** — this project is fully open source
- **Keep SMAPI updated** — security patches apply to all mods
- **Don't expose bridge files** — if you're running the MCP server remotely (not recommended), secure the file paths

---

## What This Mod Does NOT Do

- Does not access the internet
- Does not collect or transmit any data
- Does not modify save files
- Does not interact with multiplayer or other players
- Does not store any personal information

---

## Reporting Issues

If you find a security concern, please [open an issue](https://github.com/amarisaster/StardewValley-MCP/issues) on this repository.

---

## Transparency

This project is fully open source. You can audit every line of code. There are no hidden endpoints, no telemetry, no data collection.

Your game, your companions, your machine.
