# NoBoxHead — Plan de lanzamiento a tienda móvil

Estado: propuesta para aprobación. Todo lo que sigue está apoyado en lectura del código
(referencias `archivo:línea`), no en estimaciones a ojo.

Leyenda de esfuerzo: **S** = menos de 1h · **M** = unas horas · **L** = un día o más.

---

## Decisiones — RESUELTAS

| # | Decisión | Resolución |
|---|---|---|
| D1 | Armas a implementar | **Las 8**: lanzallamas, riel/láser, congelador, escopeta de fragmentación, torreta desplegable, motosierra, lanzacohetes + minas |
| D2 | Origen de la música | **Híbrido**: infraestructura + loop chiptune de relleno ahora; packs **CC0** como fuente final (curaduría del dueño) |
| D3 | Refactor de clase base | **Sí**, antes de crear contenido nuevo. Optimizar sin cambiar comportamiento |
| D4 | Filtro móvil en ajustes | Ocultar **cámara y reasignación de teclas**. Co-op local en móvil = **siempre split screen** |
| D5 | Animación | **Procedural por tween** primero (bamboleo/squash); arte multi-frame más adelante si hace falta |

### Orden de las 8 armas (D1)

Por tandas, de menor a mayor riesgo:

1. **Tanda A — reusan `Bullet`:** riel/láser (bala que perfora), escopeta de fragmentación (variante de `Shotgun`), motosierra (variante de `Knife`).
2. **Tanda B — reusan la explosión de `GrenadeProjectile`:** lanzacohetes, minas.
3. **Tanda C — mecánica nueva:** lanzallamas (daño continuo), congelador (estado alterado), torreta desplegable (entidad autónoma con IA y RPCs propios — la más cara).

---

## Fase 0 — Bugs (primero: son correctitud, no features)

### 0.1 · Proyectiles del demonio atraviesan todo — **S**
`Scenes/Entities/DemonProjectile.tscn:8` tiene `monitoring = false`, y
`DemonProjectile.cs` nunca lo activa. Un `Area2D` con monitoring apagado **no dispara
`BodyEntered` para nada**: ni paredes, ni obstáculos, ni jugadores.

Explica dos síntomas reportados a la vez: atraviesan paredes **y** nunca hicieron daño.
`Bullet.tscn` y `Grenade.tscn` no traen esa línea, por eso ellos sí colisionan.

**Arreglo:** quitar la línea del `.tscn` (o `Monitoring = true` en `_Ready`).
**Verificación:** test headless — un demonio detrás de un muro no debe dañar al jugador;
sin muro, sí.

### 0.2 · Las granadas no se ven en multijugador — **S/M**
`GrenadeProjectile` no replica su spawn por RPC, a diferencia de `Bullet`
(`Weapon.BroadcastTracer`) y del demonio (`Demon.SpawnProjectileRpc`). El mismo hueco que
ya arreglamos dos veces. Aplicar el patrón `Cosmetic` existente.

---

## Fase 1 — Mejoras rápidas y de bajo riesgo

### 1.1 · Ocultar opciones de PC en móvil — **S**
`SettingsUI.BuildUI()` (`SettingsUI.cs:56-63`) llama sus 4 secciones sin condición.
`Platform.IsMobile` (`Platform.cs:15-17`) ya existe.

- **Controles / reasignación de teclas** (`SettingsUI.cs:178-225`): 100% inútil sin teclado → envolver en `if (!Platform.IsMobile)`.
- **Mira con ratón** (`SettingsUI.cs:165`): hoy solo se deshabilita, sigue ocupando espacio → no crearlo en móvil.
- **Cámara / Split Screen**: decisión D4 — es concepto de co-op local, no estrictamente de PC.

### 1.2 · Configuración de sangre (color / desactivar) — **S**
Los 3 tonos están fijos en `BloodSystem.cs:16-18` y se usan solo en ese archivo
(líneas 63, 90, 116-121, 142). `BloodPainter.cs` ya recibe el color por parámetro: **cero cambios ahí**.

- Derivar los 3 tonos de un color base configurable.
- `Splatter()` y `Pool()` con guarda temprana si la sangre está desactivada (corta también las partículas, que se llaman desde dentro).
- Persistencia: 3 líneas en `SettingsManager` (`SaveSettings`/`LoadSettings`, patrón de `SfxVolume`).
- Por defecto: **activa y roja**, como hoy.

### 1.3 · Muro de barriles — **S** (M si se quiere rejilla)
`BarrelWeapon.BarrelFootprint = 34` (`BarrelWeapon.cs:18`) es a propósito mayor que el
barril real de 30 (`Barrel.cs:16,78`) — el comentario en la línea 17 dice literalmente
"so barrels never end up flush against each other". Eso fuerza ~2px de hueco.

- Bajar `BarrelFootprint` a ~27-28 (el margen de colisión de Godot puede rechazar 30 vs 30 exacto).
- **Riesgo real a probar:** con muros sin huecos, verificar que enemigos y jugadores no queden encajados. La lógica de `CrowdSeparation` y stuck-recovery no se afinó para muros macizos.
- Para muros *rectos* de verdad hace falta snap a rejilla — hoy la posición es continua según el ángulo de apuntado (`FindPlacementSpot`, `BarrelWeapon.cs:66-94`). Eso es **M**.

### 1.4 · Enemigos priorizan barriles sobre jugadores — **S** (tuning) / **M-L** (bien hecho)
Los barriles se recortan del nav mesh con `NavMargin = 10f` **por lado** (`Arena.cs:173`),
así que un barril de 30×30 abre un hueco de 50×50 en la malla. En un pasillo, un solo
barril puede declarar al jugador "inalcanzable" aunque el enemigo (radio 11) sí cupiera.
Ahí `IsTargetReachable()` da false y el enemigo se pasa a atacar barriles.

Además `_targetBarrel` es "pegajoso": una vez fijado, insiste hasta destruirlo, y
`FindNearestBarrel` elige el **más cercano a él**, no el que realmente bloquea el paso.

- **Rápido:** bajar `NavMargin` para barriles.
- **Correcto:** elegir el barril que está sobre la ruta hacia el jugador, y re-evaluar.

---

## Fase 2 — Preparación de tienda (config + arte, sin lógica de juego)

Estado actual verificado: `export_presets.cfg` ya tiene preset Android con
`com.fdlg.noboxhead`, versión 1.0.0, permisos de red correctos y solo `arm64-v8a`.
Ya existe un APK **debug** de ~102 MB.

| Ítem | Estado | Esfuerzo |
|---|---|---|
| Ícono propio | **HECHO** — generado por `Tools/generate_app_icon.py`, los 4 slots conectados | — |
| Pantalla de carga | **HECHO** — `splash.png` + fondo del color del juego, boot splash de Godot desactivado | — |
| Formato **AAB** | **HECHO** — `export_format=1` (`use_gradle_build` ya estaba en true) | — |
| Keystore de release + firma | **PENDIENTE — TUYO.** Ver abajo | S |
| `min_sdk` / `target_sdk` explícitos | **PENDIENTE a propósito** — ver abajo | S |
| Preset iOS | No existe | M (requiere cuenta Apple) |

### Ícono — cómo regenerarlo

El arte se dibuja por código, igual que los sprites de armas y munición, así que es reproducible
y versionable como fuente en vez de como binario opaco:

```
python Tools/generate_app_icon.py
```

Genera en `Assets/Icon/`: `icon_512.png` (proyecto + ficha de tienda), `icon_192.png` (launcher
legacy), `adaptive_foreground/background/monochrome.png` (432×432, el foreground encogido al
66% porque Android puede recortar hasta ahí) y `splash.png`. Después reimportar en Godot.

### Keystore — lo tenés que hacer vos

No genero claves de firma: son una credencial tuya y quien la tenga puede publicar
actualizaciones de tu app. El comando es:

```bash
keytool -genkey -v -keystore noboxhead-release.keystore -alias noboxhead -keyalg RSA -keysize 2048 -validity 10000
```

Guardalo FUERA del repositorio y nunca lo commitees. Luego se configura en el preset de Android
(o en las opciones de exportación del editor). Si lo perdés, no podés volver a actualizar la app
publicada — Google no lo puede recuperar.

### min_sdk / target_sdk — por qué los dejé en blanco

Están vacíos y Godot usa sus defaults. Pinearlos a mano es fácil, pero un valor equivocado
rompe la compilación o hace que Play rechace el bundle, y no puedo verificar desde el repo qué
SDK tenés instalado ni cuál es el mínimo que Play exige hoy. Decime qué versión querés y los
fijo, o dejalos como están y que resuelva el exportador.

---

## Fase 3 — Música

`AudioManager.cs` es **solo SFX**: pool fijo de 16 voces, un único bus Master, un solo
`SfxVolume`. **No existe carpeta `Assets/Audio/Music/` ni un solo track.**

- 3.1 Bus "Music" + 2 `AudioStreamPlayer` para crossfade + `MusicVolume` en settings — **S**
- 3.2 Crossfade por tween entre los dos players — **S**
- 3.3 Selector de pista en el menú (mismo patrón `OptionButton` que Arena/Skin) — **S**
- 3.4 **Conseguir la música** — **M**, y es decisión **D2**

Sobre D2: `Tools/generate_sfx.py` sintetiza los efectos actuales desde cero, pero solo
sirve para sonidos cortos — no produce música loopeable. Opciones:
**(a)** extender el generador a chiptune procedural (control total, calidad limitada),
**(b)** packs CC0 de 8-bit rock/metal, **(c)** encargar los tracks.
Yo no puedo descargar ni generar música con licencia; necesito que elijas la vía.

---

## Fase 4 — Armas y explosivos

Añadir un arma toca **9 puntos de registro** (clase, escena de proyectil, sprite de mano en
`Player.cs`, sprite de munición en `AmmoPack.cs`, `ScoreManager.WeaponIdToName`,
`AmmoDropWeight`, `Unlocks`, el `switch` de `Arena.cs:547-554`, y `AddWeapon` si es inicial).
El propio código avisa que `ScoreManager.cs:19` y el switch de `Arena.cs` deben mantenerse
sincronizados a mano, sin red de seguridad del compilador.

### 4.1 · Minas y explosivos — **M**
Ya existen las dos mitades: `BarrelWeapon` aporta la lógica de colocación
(`FindPlacementSpot`/`CanPlaceAt`) y `GrenadeProjectile.Explode()` el daño en área
(radio 135, falloff hasta 50%). Una mina = colocación + radio de disparo + esa explosión.

### 4.2 · Tres armas nuevas — **M** cada una — **decisión D1**

Propuestas con *efectos distintos*, no variantes de estadísticas:

| Arma | Efecto | Por qué encaja |
|---|---|---|
| **Lanzallamas** | Cono corto, daño continuo por quemadura | Reusa `Bullet` con `MaxDistance` corto y cadencia alta; el DoT es nuevo |
| **Riel / Láser** | Perfora en línea recta a varios enemigos, cadencia lenta | Solo requiere que la bala no se destruya al primer impacto |
| **Congelador** | Ralentiza (multiplicador de `MoveSpeed` + tinte azul) | Introduce estados alterados, útil también para bosses |

Alternativas si preferís otra cosa: escopeta de fragmentación, torreta desplegable,
motosierra (mejora del cuchillo), lanzacohetes.

---

## Fase 5 — Minibosses y bosses

**Realidad actual:** el Ogro no es un miniboss, es un zombi con estadísticas escaladas
(vida 320, velocidad 20, resistencia a knockback ×0.35). No tiene fases, ni ataques
propios, ni barra de vida en pantalla, ni entrada especial.

### 5.1 · Refactor: clase base de enemigos — **M** — **decisión D3**
`Enemy`, `Sprinter`, `Ogre` y `Demon` son **cuatro archivos de ~300 líneas casi idénticos**,
sin herencia. Cada uno reimplementa navegación, stuck-recovery, separación, barriles,
knockback, y el **cuarteto de RPCs** (reenvío de daño, sync visual, muerte, sync de posición).

Recomiendo hacerlo **antes** de añadir bosses. Si no, cada enemigo nuevo son 300 líneas
copiadas y basta olvidar un RPC para tener el clásico "funciona solo, se rompe en online".
Ese bug ya nos costó varias vueltas en esta sesión.

### 5.2 · Infraestructura de boss — **L**
No existe nada de esto hoy: máquina de estados / fases, barra de vida de boss en el HUD
(`HUD.cs` solo tiene barras flotantes por enemigo), oleada dedicada de boss en `GameManager`
(solo hay `CurrentWave`/`EnemiesRemaining`), y disparador de aparición.

### 5.3 · Cambio de música en boss — **S** — depende de 3.1 y 5.2
Un hook en el spawn del boss → `CrossfadeTo("boss")`.

---

## Fase 6 — Pulido visual

### 6.1 · Menú principal con gameplay de fondo — **M**
Cargar la Arena en un `SubViewport` detrás del menú, con IA jugando sola o cámara en
paneo, y el menú encima en su `CanvasLayer`. Cuidado con el coste en móvil: conviene
limitar oleadas y efectos, o usar un loop pregrabado.

### 6.2 · Animaciones de movimiento — **L** — **decisión D5**
**No existe infraestructura de animación**: cero usos de `AnimatedSprite2D` o `SpriteFrames`
en todo el proyecto, y **el arte son PNGs de un solo cuadro** (4 enemigos, 6 skins de
jugador) — no hay hojas de sprites ni frames `_00/_01`.

Alcance real: los **5 cuerpos** con movimiento direccional (Enemy, Sprinter, Ogre, Demon,
Player). Los pickups y proyectiles son estáticos o con tween.

Lo que hay que rehacer por entidad: el offset está calculado a mano contra el tamaño
específico de cada PNG (ej. `Enemy.cs:316-317`, canvas 480×580, centro 239,301) — con
hojas de sprites hay que recalcularlo. Además `SyncEnemyState` solo lleva posición,
rotación y vida: si la animación debe verse igual en todos los peers, hay que ampliarlo.

Decisión D5: **(a)** arte multi-frame real (mejor resultado, requiere dibujar 5 ciclos) o
**(b)** efecto procedural por tween (bamboleo/squash al caminar) — mucho más barato y
aprovecha el arte actual.

---

## Orden de ejecución propuesto

```
Fase 0  Bugs                     ──> correctitud, desbloquea pruebas fiables
Fase 1  Quick wins               ──> valor visible, riesgo bajo
Fase 2  Preparación de tienda    ──> se puede hacer en paralelo (config + arte)
Fase 3  Música (infra)           ──> 3.4 depende de D2
Fase 5.1 Refactor clase base     ──> ANTES de crear contenido nuevo
Fase 4  Armas + minas            ──> depende de D1
Fase 5.2/5.3 Bosses + música     ──> depende de 3.1 y 5.1
Fase 6  Pulido                   ──> lo más caro, al final
```

Fases 0, 1 y 2 son independientes entre sí y se pueden repartir en paralelo.
