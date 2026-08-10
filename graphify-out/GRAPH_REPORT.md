# Graph Report - .  (2026-08-05)

## Corpus Check
- 112 files · ~51,053 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 716 nodes · 1327 edges · 32 communities (30 shown, 2 thin omitted)
- Extraction: 97% EXTRACTED · 3% INFERRED · 0% AMBIGUOUS · INFERRED: 42 edges (avg confidence: 0.63)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- HUD & Touch Controls Layout
- Namespace & Weapon Placement
- Player State & Networking
- LAN Discovery & ENet Peers
- Arena Build & Navigation
- Wave & Game-Over Orchestration
- Ammo Pickups & Projectiles
- Sound Effect Generation
- Zombie AI & Crowd Separation
- Settings Screen Sections
- Audio Playback & Voices
- Main Menu & Player Skins
- Arena Layout Definitions
- Split-Screen Camera
- Lobby Host/Join Flow
- Demon Enemy
- Ogre Mini-Boss
- Sprinter Enemy
- Blood Decals & Painting
- Settings Persistence & Bindings
- Virtual Joystick Input
- Placeable Barrels
- Health Pickups
- Bullets & Damage Interface
- Weapon Sprite Generation
- Ammo Sprite Generation
- Godot MCP Server Config
- Knockback Interface
- Build Targets & SDK
- Player Skin Generation

## God Nodes (most connected - your core abstractions)
1. `Player` - 56 edges
2. `HUD` - 53 edges
3. `Arena` - 46 edges
4. `NoBoxHead` - 40 edges
5. `NetworkManager` - 35 edges
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

## Communities (32 total, 2 thin omitted)

### Community 0 - "HUD & Touch Controls Layout"
Cohesion: 0.05
Nodes (22): CanvasLayer, Content, HSlider, PanelContainer, Root, Action, Button, Color (+14 more)

### Community 1 - "Namespace & Weapon Placement"
Cohesion: 0.05
Nodes (24): NoBoxHead, float, Node, PackedScene, Vector2, BarrelWeapon, PackedScene, Vector2 (+16 more)

### Community 2 - "Player State & Networking"
Cohesion: 0.09
Nodes (13): Node2D, bool, Color, ColorRect, Dictionary, float, InputEvent, int (+5 more)

### Community 3 - "LAN Discovery & ENet Peers"
Cohesion: 0.07
Nodes (16): Broadcast, CancellationToken, CancellationTokenSource, ENetMultiplayerPeer, Error, IPAddress, Local, Dictionary (+8 more)

### Community 4 - "Arena Build & Navigation"
Cohesion: 0.10
Nodes (11): NavigationRegion2D, bool, Color, Control, float, HashSet, List, PackedScene (+3 more)

### Community 5 - "Wave & Game-Over Orchestration"
Cohesion: 0.08
Nodes (13): NodePath, List, Rpc, Vector2, GameManager, bool, int, List (+5 more)

### Community 6 - "Ammo Pickups & Projectiles"
Cohesion: 0.08
Nodes (14): Area2D, bool, Dictionary, int, Node2D, string, AmmoPack, float (+6 more)

### Community 7 - "Sound Effect Generation"
Cohesion: 0.30
Nodes (28): apply(), attack(), barrel_place(), decay(), enemy_death(), explosion(), game_over(), highpass() (+20 more)

### Community 8 - "Zombie AI & Crowd Separation"
Cohesion: 0.12
Nodes (12): float, Node2D, Vector2, CrowdSeparation, bool, ColorRect, float, NavigationAgent2D (+4 more)

### Community 9 - "Settings Screen Sections"
Cohesion: 0.18
Nodes (8): HorizontalAlignment, Button, Dictionary, InputEvent, Label, string, VBoxContainer, SettingsUI

### Community 10 - "Audio Playback & Voices"
Cohesion: 0.09
Nodes (12): AudioStreamPlayer, Node, Dictionary, int, List, string, AudioManager, Dictionary (+4 more)

### Community 11 - "Main Menu & Player Skins"
Cohesion: 0.15
Nodes (7): Control, PlayerSkins, Skin, Action, MainMenuUI, Skin, Texture2D

### Community 12 - "Arena Layout Definitions"
Cohesion: 0.15
Nodes (12): C, Center, S, Color, float, List, Vector2, ArenaLayouts (+4 more)

### Community 13 - "Split-Screen Camera"
Cohesion: 0.15
Nodes (9): Camera2D, Control, float, List, Node2D, Vector2, CameraManager, Platform (+1 more)

### Community 14 - "Lobby Host/Join Flow"
Cohesion: 0.12
Nodes (9): LineEdit, bool, Button, int, Label, List, VBoxContainer, LobbyMode (+1 more)

### Community 15 - "Demon Enemy"
Cohesion: 0.15
Nodes (9): bool, ColorRect, float, NavigationAgent2D, Node, Rpc, Sprite2D, Vector2 (+1 more)

### Community 16 - "Ogre Mini-Boss"
Cohesion: 0.16
Nodes (9): CharacterBody2D, bool, ColorRect, float, NavigationAgent2D, Rpc, Sprite2D, Vector2 (+1 more)

### Community 17 - "Sprinter Enemy"
Cohesion: 0.17
Nodes (8): bool, ColorRect, float, NavigationAgent2D, Rpc, Sprite2D, Vector2, Sprinter

### Community 18 - "Blood Decals & Painting"
Cohesion: 0.16
Nodes (9): Node2D, Color, List, Vector2, BloodPainter, Color, Vector2, BloodSystem (+1 more)

### Community 19 - "Settings Persistence & Bindings"
Cohesion: 0.20
Nodes (8): ConfigFile, Key, Dictionary, string, AimMode, CameraMode, GameMode, SettingsManager

### Community 20 - "Virtual Joystick Input"
Cohesion: 0.18
Nodes (8): InputEventScreenDrag, InputEventScreenTouch, ColorRect, float, InputEvent, int, Vector2, VirtualJoystick

### Community 21 - "Placeable Barrels"
Cohesion: 0.19
Nodes (7): bool, Color, ColorRect, float, Vector2, Barrel, StaticBody2D

### Community 22 - "Health Pickups"
Cohesion: 0.20
Nodes (7): bool, Color, float, Node, Node2D, Vector2, HealthPack

### Community 23 - "Bullets & Damage Interface"
Cohesion: 0.18
Nodes (5): float, Node, Vector2, Bullet, IDamageable

### Community 24 - "Weapon Sprite Generation"
Cohesion: 0.38
Nodes (11): barrel(), box(), grenade(), knife(), machinegun(), new_canvas(), pistol(), poly() (+3 more)

### Community 25 - "Ammo Sprite Generation"
Cohesion: 0.40
Nodes (10): barrel(), box(), bullet(), grenade(), machinegun(), new_canvas(), pistol(), Generates ammo pickup sprites in Assets/Sprites/Ammo, in the same blocky thick-… (+2 more)

### Community 26 - "Godot MCP Server Config"
Cohesion: 0.50
Nodes (3): npx, godot, @coding-solo/godot-mcp

### Community 28 - "Build Targets & SDK"
Cohesion: 0.67
Nodes (3): net9.0, NoBoxHead, Godot.NET.Sdk/4.7.0

## Knowledge Gaps
- **6 isolated node(s):** `npx`, `@coding-solo/godot-mcp`, `net9.0`, `Godot.NET.Sdk/4.7.0`, `Skin` (+1 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **2 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `NoBoxHead` connect `Namespace & Weapon Placement` to `LAN Discovery & ENet Peers`, `Wave & Game-Over Orchestration`, `Ammo Pickups & Projectiles`, `Zombie AI & Crowd Separation`, `Audio Playback & Voices`, `Main Menu & Player Skins`, `Arena Layout Definitions`, `Split-Screen Camera`, `Lobby Host/Join Flow`, `Demon Enemy`, `Ogre Mini-Boss`, `Sprinter Enemy`, `Blood Decals & Painting`, `Settings Persistence & Bindings`, `Virtual Joystick Input`, `Placeable Barrels`, `Health Pickups`, `Bullets & Damage Interface`, `Knockback Interface`?**
  _High betweenness centrality (0.316) - this node is a cross-community bridge._
- **Why does `Arena` connect `Arena Build & Navigation` to `HUD & Touch Controls Layout`, `Namespace & Weapon Placement`, `Player State & Networking`, `Arena Layout Definitions`, `Split-Screen Camera`, `Blood Decals & Painting`, `Placeable Barrels`?**
  _High betweenness centrality (0.191) - this node is a cross-community bridge._
- **Why does `Player` connect `Player State & Networking` to `HUD & Touch Controls Layout`, `Namespace & Weapon Placement`, `Arena Build & Navigation`, `Wave & Game-Over Orchestration`, `Ammo Pickups & Projectiles`, `Ogre Mini-Boss`, `Virtual Joystick Input`?**
  _High betweenness centrality (0.186) - this node is a cross-community bridge._
- **What connects `npx`, `@coding-solo/godot-mcp`, `net9.0` to the rest of the system?**
  _6 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `HUD & Touch Controls Layout` be split into smaller, more focused modules?**
  _Cohesion score 0.05499735589635114 - nodes in this community are weakly interconnected._
- **Should `Namespace & Weapon Placement` be split into smaller, more focused modules?**
  _Cohesion score 0.05075187969924812 - nodes in this community are weakly interconnected._
- **Should `Player State & Networking` be split into smaller, more focused modules?**
  _Cohesion score 0.08562367864693446 - nodes in this community are weakly interconnected._