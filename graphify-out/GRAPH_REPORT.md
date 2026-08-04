# Graph Report - .  (2026-08-04)

## Corpus Check
- Corpus is ~39,946 words - fits in a single context window. You may not need a graph.

## Summary
- 692 nodes · 1278 edges · 35 communities (32 shown, 3 thin omitted)
- Extraction: 97% EXTRACTED · 3% INFERRED · 0% AMBIGUOUS · INFERRED: 41 edges (avg confidence: 0.62)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- HUD & Pause UI
- GameManager & Player Core
- Menu, Lobby & Skins UI
- Projectiles & Pickups
- Networking (NetworkManager)
- Arena Construction & Lifecycle
- Barrel & Ogre (mini-boss)
- SFX Generation Tool
- Settings UI
- Audio & Score Autoloads
- Demon Enemy
- Enemy (Zombie) AI
- Camera & Split-Screen
- Sprinter Enemy
- Blood/Gore Effects
- Settings Persistence
- Arena Layout Generation
- Virtual Joystick (Touch Input)
- Wave Spawner
- Health Pack Pickup
- Core Namespace / Cross-Module Links
- Weapon Sprite Generation Tool
- Ammo Sprite Generation Tool
- Barrel Weapon (Placeable)
- Weapon Base Class
- Weapon Initialization
- Knife (Melee Weapon)
- Shotgun Weapon
- Grenade Launcher Weapon
- Godot MCP Tooling Config
- IKnockbackable Interface
- Project Build Config
- Player Skin Generation Tool

## God Nodes (most connected - your core abstractions)
1. `Player` - 53 edges
2. `HUD` - 53 edges
3. `Arena` - 43 edges
4. `NoBoxHead` - 38 edges
5. `NetworkManager` - 34 edges
6. `Demon` - 26 edges
7. `Barrel` - 24 edges
8. `SettingsUI` - 24 edges
9. `Enemy` - 23 edges
10. `Ogre` - 23 edges

## Surprising Connections (you probably didn't know these)
- `Arena` --references--> `CameraManager`  [EXTRACTED]
  Scripts/Arena/Arena.cs → Scripts/Camera/CameraManager.cs
- `Arena` --references--> `Player`  [EXTRACTED]
  Scripts/Arena/Arena.cs → Scripts/Entities/Player.cs
- `Arena` --references--> `HUD`  [EXTRACTED]
  Scripts/Arena/Arena.cs → Scripts/UI/HUD.cs
- `Barrel` --references--> `Arena`  [EXTRACTED]
  Scripts/Entities/Barrel.cs → Scripts/Arena/Arena.cs
- `BarrelWeapon` --references--> `Arena`  [EXTRACTED]
  Scripts/Weapons/BarrelWeapon.cs → Scripts/Arena/Arena.cs

## Import Cycles
- None detected.

## Communities (35 total, 3 thin omitted)

### Community 0 - "HUD & Pause UI"
Cohesion: 0.06
Nodes (22): CanvasLayer, Content, HSlider, PanelContainer, Root, Action, Button, Color (+14 more)

### Community 1 - "GameManager & Player Core"
Cohesion: 0.06
Nodes (16): List, Rpc, Vector2, GameManager, bool, Color, ColorRect, Dictionary (+8 more)

### Community 2 - "Menu, Lobby & Skins UI"
Cohesion: 0.07
Nodes (15): Control, LineEdit, PlayerSkins, Skin, bool, Button, int, Label (+7 more)

### Community 3 - "Projectiles & Pickups"
Cohesion: 0.05
Nodes (20): Area2D, bool, Dictionary, int, Node2D, string, AmmoPack, float (+12 more)

### Community 4 - "Networking (NetworkManager)"
Cohesion: 0.08
Nodes (13): CancellationToken, CancellationTokenSource, ENetMultiplayerPeer, Error, Dictionary, int, List, Rpc (+5 more)

### Community 5 - "Arena Construction & Lifecycle"
Cohesion: 0.09
Nodes (10): NavigationRegion2D, Color, Control, float, List, Node, PackedScene, Rpc (+2 more)

### Community 6 - "Barrel & Ogre (mini-boss)"
Cohesion: 0.09
Nodes (15): bool, Color, ColorRect, float, Vector2, Barrel, bool, ColorRect (+7 more)

### Community 7 - "SFX Generation Tool"
Cohesion: 0.30
Nodes (28): apply(), attack(), barrel_place(), decay(), enemy_death(), explosion(), game_over(), highpass() (+20 more)

### Community 8 - "Settings UI"
Cohesion: 0.18
Nodes (8): HorizontalAlignment, Button, Dictionary, InputEvent, Label, string, VBoxContainer, SettingsUI

### Community 9 - "Audio & Score Autoloads"
Cohesion: 0.09
Nodes (12): AudioStreamPlayer, HashSet, Node, Dictionary, int, List, string, AudioManager (+4 more)

### Community 10 - "Demon Enemy"
Cohesion: 0.16
Nodes (9): bool, ColorRect, float, NavigationAgent2D, Node, Rpc, Sprite2D, Vector2 (+1 more)

### Community 11 - "Enemy (Zombie) AI"
Cohesion: 0.16
Nodes (9): CharacterBody2D, bool, ColorRect, float, NavigationAgent2D, Rpc, Sprite2D, Vector2 (+1 more)

### Community 12 - "Camera & Split-Screen"
Cohesion: 0.18
Nodes (8): Camera2D, Control, float, List, Node2D, Vector2, CameraManager, SubViewportContainer

### Community 13 - "Sprinter Enemy"
Cohesion: 0.17
Nodes (8): bool, ColorRect, float, NavigationAgent2D, Rpc, Sprite2D, Vector2, Sprinter

### Community 14 - "Blood/Gore Effects"
Cohesion: 0.16
Nodes (9): Node2D, Color, List, Vector2, BloodPainter, Color, Vector2, BloodSystem (+1 more)

### Community 15 - "Settings Persistence"
Cohesion: 0.20
Nodes (8): ConfigFile, Key, Dictionary, string, AimMode, CameraMode, GameMode, SettingsManager

### Community 16 - "Arena Layout Generation"
Cohesion: 0.20
Nodes (10): C, Center, S, Color, float, List, Vector2, ArenaLayouts (+2 more)

### Community 17 - "Virtual Joystick (Touch Input)"
Cohesion: 0.18
Nodes (8): InputEventScreenDrag, InputEventScreenTouch, ColorRect, float, InputEvent, int, Vector2, VirtualJoystick

### Community 18 - "Wave Spawner"
Cohesion: 0.22
Nodes (8): NodePath, bool, int, List, PackedScene, Rpc, Vector2, WaveSpawner

### Community 19 - "Health Pack Pickup"
Cohesion: 0.20
Nodes (7): bool, Color, float, Node, Node2D, Vector2, HealthPack

### Community 20 - "Core Namespace / Cross-Module Links"
Cohesion: 0.17
Nodes (3): NoBoxHead, Platform, LobbyMode

### Community 21 - "Weapon Sprite Generation Tool"
Cohesion: 0.38
Nodes (11): barrel(), box(), grenade(), knife(), machinegun(), new_canvas(), pistol(), poly() (+3 more)

### Community 22 - "Ammo Sprite Generation Tool"
Cohesion: 0.40
Nodes (10): barrel(), box(), bullet(), grenade(), machinegun(), new_canvas(), pistol(), Generates ammo pickup sprites in Assets/Sprites/Ammo, in the same blocky thick-… (+2 more)

### Community 23 - "Barrel Weapon (Placeable)"
Cohesion: 0.33
Nodes (5): float, Node, PackedScene, Vector2, BarrelWeapon

### Community 24 - "Weapon Base Class"
Cohesion: 0.27
Nodes (5): float, int, PackedScene, Vector2, Weapon

### Community 26 - "Knife (Melee Weapon)"
Cohesion: 0.36
Nodes (4): Action, float, Vector2, Knife

### Community 27 - "Shotgun Weapon"
Cohesion: 0.29
Nodes (4): float, int, Vector2, Shotgun

### Community 28 - "Grenade Launcher Weapon"
Cohesion: 0.33
Nodes (3): PackedScene, Vector2, GrenadeLauncher

### Community 29 - "Godot MCP Tooling Config"
Cohesion: 0.50
Nodes (3): npx, godot, @coding-solo/godot-mcp

### Community 31 - "Project Build Config"
Cohesion: 0.67
Nodes (3): net9.0, NoBoxHead, Godot.NET.Sdk/4.7.0

## Knowledge Gaps
- **6 isolated node(s):** `npx`, `@coding-solo/godot-mcp`, `net9.0`, `Godot.NET.Sdk/4.7.0`, `Skin` (+1 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **3 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `NoBoxHead` connect `Core Namespace / Cross-Module Links` to `GameManager & Player Core`, `Menu, Lobby & Skins UI`, `Projectiles & Pickups`, `Networking (NetworkManager)`, `Barrel & Ogre (mini-boss)`, `Audio & Score Autoloads`, `Enemy (Zombie) AI`, `Camera & Split-Screen`, `Sprinter Enemy`, `Blood/Gore Effects`, `Settings Persistence`, `Arena Layout Generation`, `Virtual Joystick (Touch Input)`, `Wave Spawner`, `Health Pack Pickup`, `Barrel Weapon (Placeable)`, `Weapon Initialization`, `Knife (Melee Weapon)`, `Shotgun Weapon`, `Grenade Launcher Weapon`, `IKnockbackable Interface`?**
  _High betweenness centrality (0.311) - this node is a cross-community bridge._
- **Why does `Arena` connect `Arena Construction & Lifecycle` to `HUD & Pause UI`, `GameManager & Player Core`, `Barrel & Ogre (mini-boss)`, `Camera & Split-Screen`, `Blood/Gore Effects`, `Arena Layout Generation`, `Core Namespace / Cross-Module Links`, `Barrel Weapon (Placeable)`?**
  _High betweenness centrality (0.193) - this node is a cross-community bridge._
- **Why does `Player` connect `GameManager & Player Core` to `HUD & Pause UI`, `Projectiles & Pickups`, `Arena Construction & Lifecycle`, `Enemy (Zombie) AI`, `Virtual Joystick (Touch Input)`, `Core Namespace / Cross-Module Links`, `Weapon Base Class`?**
  _High betweenness centrality (0.186) - this node is a cross-community bridge._
- **What connects `npx`, `@coding-solo/godot-mcp`, `net9.0` to the rest of the system?**
  _6 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `HUD & Pause UI` be split into smaller, more focused modules?**
  _Cohesion score 0.05706214689265537 - nodes in this community are weakly interconnected._
- **Should `GameManager & Player Core` be split into smaller, more focused modules?**
  _Cohesion score 0.06015037593984962 - nodes in this community are weakly interconnected._
- **Should `Menu, Lobby & Skins UI` be split into smaller, more focused modules?**
  _Cohesion score 0.06845513413506013 - nodes in this community are weakly interconnected._