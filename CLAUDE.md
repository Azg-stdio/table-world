# CLAUDE.md — Tabletop agéntico (nombre provisional)

Este archivo es el contexto permanente del proyecto para Claude Code. Léelo completo antes de trabajar. Si una decisión de aquí cambia, actualiza este archivo en el mismo commit.

---

## 1. Qué estamos construyendo

Un simulador de juegos de mesa 3D, posicionado como "un Tabletop Simulator más avanzado, con la posibilidad de conectar un agente de IA".

- **Agente creador de juegos (núcleo del producto):** el host entrega el reglamento y los assets de su juego; el agente arma la lógica y prepara la mesa, y hace preguntas cuando el reglamento es ambiguo.
- **El creador es el host** de la partida. Los jugadores reciben los assets al unirse.
- **Personajes con físicas** tipo Peak / REPO como gancho (fase posterior).
- **Asistente de DnD** para planear sesiones y ambientar (luces, VFX, SFX, música). Las acciones en vivo son sugerencias que el DM confirma, nunca cambios automáticos (fase posterior).
- **Largo plazo:** campañas de DnD con inmersión 3D completa. Fuera del alcance actual.

**Público inicial:** diseñadores indie de juegos de mesa que prototipan y testean online.

## 2. Stack y decisiones cerradas

| Tema | Decisión |
|---|---|
| Motor | **Unity 6.6** (Supported). Migrar a **6.7 LTS** cuando salga. No usar 6.0 ni 6.3 |
| Plataforma | **Windows Build Support** (.exe para Steam / itch.io). NO UWP. Mac y Linux más adelante |
| Backend de scripting | **Mono** para desarrollo. IL2CPP se evalúa para builds de lanzamiento |
| Render | **URP** |
| Física | **PhysX** (la integrada) |
| Lenguaje | C# |
| IA | El juego expone un **servidor MCP**; el usuario lo maneja con su propio Claude Code / Claude / Codex. No se hace login con suscripciones de terceros ni se usan API keys dentro del juego por ahora (las políticas de Anthropic y OpenAI cambiaron varias veces en 2026; revisar antes de añadir esa opción) |
| Reglas de los juegos | **Formato declarativo propio** (JSON con esquema) que interpreta nuestro motor de reglas. Para lógica que no quepa ahí: **Lua con sandbox** (MoonSharp o equivalente), con acceso solo a la API que expongamos |
| Mods / catálogo | Después del prototipo. Candidatos: Steam Workshop (Steamworks.NET) o mod.io |
| Importador de TTS | Después del prototipo. Ver sección 6 |

## 3. Arquitectura

```
/
├── CLAUDE.md
├── core/                   # Librería C# pura (.NET Standard 2.1). CERO dependencias de Unity
│   ├── Rules/              # Esquema del formato de reglas, modelo de mesa, validación
│   ├── Engine/             # Motor de reglas: estado, turnos, fases, acciones, condiciones
│   ├── Scripting/          # Sandbox Lua y API expuesta
│   └── Tts/                # Lector del formato de TTS y mapeo a nuestro modelo (fase posterior)
├── core.tests/             # Tests xUnit del core. Se corren con `dotnet test`, sin abrir Unity
├── mcp-server/             # Servidor MCP (proceso aparte). Habla en términos de juego de mesa
├── unity/                  # Proyecto Unity: render, física, red, UI, muñecos
│   └── Assets/
│       ├── _Project/       # Todo nuestro código y assets viven aquí
│       │   ├── Scripts/
│       │   ├── Prefabs/
│       │   ├── Scenes/
│       │   └── Settings/
│       └── Plugins/        # core.dll y dependencias
├── tools/                  # Scripts auxiliares (análisis de TTS, conversores)
└── docs/                   # Decisiones (ADR), esquema de reglas, tabla de equivalencias con TTS
```

**Principios:**

1. **El core no conoce Unity.** El formato de reglas, el motor y el MCP deben funcionar y probarse con `dotnet test`. Unity solo renderiza, simula física, maneja red y UI. Si algo del core necesita Unity, está mal diseñado.
2. **El MCP habla de juegos de mesa, no de Unity.** Herramientas tipo `create_deck`, `define_zone`, `add_phase`, `set_win_condition`, `place_object`, nunca `create_gameobject` o `set_transform`. Se comunica con Unity por WebSocket local.
3. **Host autoritativo.** El host ejecuta el motor de reglas; los clientes envían intenciones y reciben estado.
4. **Nunca se ejecuta C# generado por usuarios o por el agente en la build pública.** Solo JSON validado y Lua en sandbox.
5. **Compatible con TTS en el modelo de datos, no un clon en el comportamiento.** Las primitivas deben mapear uno a uno con las de TTS (ver sección 6), pero no copiamos su física exacta, su API Lua completa ni su interfaz.

## 4. Reglas para trabajar con agentes en Unity

- **No edites a mano archivos `.unity`, `.prefab`, `.asset` ni `.meta`.** Crea y modifica escenas y prefabs desde código (scripts de editor en `Assets/_Project/Editor/`) o a través del **Unity CLI** (`unity command`, `unity eval`) con el Editor abierto. Si algo solo se puede hacer en el editor, dilo y pide que lo haga el humano.
- **Herramientas de agente para Unity:** Unity CLI (`winget install Unity.CLI`) + paquete `com.unity.pipeline` en el proyecto + plugin oficial `unity@unity-agent-plugin` de Claude Code (habilitado en `.claude/settings.json`). Preferir comandos directos del CLI sobre `unity mcp` (más rápidos y con menos tokens). El CLI y el paquete están en beta/experimental: revisar las release notes antes de actualizar. Analytics del CLI desactivado (`unity analytics opt-out`).
- Antes de proponer un paquete, verifica que sea compatible con Unity 6.6 y URP.
- Unity usa **Fast Enter Play Mode** por defecto en 6.6: no uses estado estático que dependa de recargar el dominio. Inicializa con `[RuntimeInitializeOnLoadMethod]` o en `Awake`.
- Pon la lógica de juego en `core/` y escribe su test antes de tocar Unity.
- Nombres: PascalCase para tipos y métodos, camelCase para campos privados con `_`, inglés para código, español o inglés para documentación.
- Commits pequeños y descriptivos. Un cambio de arquitectura va acompañado de un ADR en `docs/adr/`.

## 5. Primitivas del modelo de mesa (v0)

Deben cubrir las de TTS para que el importador sea un traductor directo:

- **Objetos:** `Card`, `Deck` (con sprite sheet: columnas × filas, cara y dorso), `Token`, `Tile`, `Die` (n caras), `Model` (glTF), `Bag`, `InfiniteBag`, `Board`, `Figure`
- **Zonas:** `HandZone` (por jugador, oculta), `HiddenZone`, `ScriptingZone`, `SnapPoint`
- **Propiedades comunes:** `id` estable, `name`, `description`, `gmNotes`, `tags`, `locked`, `states` (varios estados por objeto), `transform`, `container` (objeto padre), `script` (Lua opcional)
- **Jugadores:** colores de jugador, asientos, roles (host, jugador, espectador)
- **Reglas:** fases, turnos, acciones permitidas por fase, recursos, condiciones de victoria, eventos (`onTurnStart`, `onDrop`, `onEnterZone`, etc.)
- **Acciones de mesa estándar:** voltear, barajar, buscar, repartir, bloquear, agrupar, medir, tirar dados
- **UI declarativa:** paneles, botones y contadores (equivalente a XmlUI de TTS)
- **Unidades:** definir UNA constante de conversión con TTS, documentada en `docs/tts-mapping.md`, y tamaños por defecto similares (carta estándar, ficha, dado)

## 6. Lo que sabemos de los mods de TTS

Análisis de la colección local (6 mods del Workshop, 26 partidas, 37 objetos; script en `tools/tts-analysis/`):

- **AssetBundles no son decoración:** 4 de 6 mods los usan y 3 dependen de ellos (son las figuras del juego). Hay `Custom_Assetbundle` y `Custom_Assetbundle_Bag` (contenedor y modelo a la vez).
- **Lua global en 5 de 6, XmlUI en 6 de 6.** Snap points y zonas (`ScriptingTrigger`, `HandTrigger`) en 5 de 6.
- **Funciones de la API más usadas:** `getObjectFromGUID`, `getGMNotes`, `Wait.time`, `getObjects`, `getName`, `getPosition`. Ese es el orden de prioridad para la capa de compatibilidad Lua.
- **94% de las URLs** están en el CDN de Steam. El importador debe descargar y cachear los assets localmente.
- La muestra es pequeña y sesgada a mods pesados. Pendiente: analizar 20–30 mods variados.

**Reglas del importador (cuando se construya):**
- Corre localmente sobre archivos que el usuario ya tiene. El resultado es privado por defecto.
- Nunca raspa ni republica el Workshop.
- Cada AssetBundle que no pueda convertirse se reemplaza por un placeholder que **conserva** id, nombre, GM notes, contenedor, posición y script.
- Los AssetBundles dependen de la versión de Unity y del pipeline con que se crearon; ver Tarea 0.

## 7. Copyright y contenido

- Catálogo en capas: oficiales/licenciados (promocionados), originales (nicho principal), comunidad (reporte y retiro, sin promoción algorítmica).
- Publicar en el catálogo requiere declarar autoría o derechos. Jugar en privado no.
- Mencionar "Tabletop Simulator" solo para describir compatibilidad de formato; sin logos ni implicar alianza.
- Antes de lanzar: revisión legal.

## 8. Hoja de ruta

### Tarea 0 — Prueba de AssetBundles y pipeline (primera semana)
Decide el pipeline de render y cuánto invertir en el importador.
1. Proyecto Unity 6.6 vacío con URP. Intentar cargar 3–5 AssetBundles de los mods locales (Pokémon y Chimera Table). ¿Cargan? ¿Materiales correctos o rosados?
2. Repetir con el pipeline Built-in.
3. Identificar la versión de Unity con que se crearon (encabezado del archivo) y probar con esa versión si es distinta.
4. Probar AssetRipper sobre los mismos bundles y evaluar el glTF resultante. AssetRipper es GPL: solo como herramienta externa, nunca dentro del juego.
5. Escribir el resultado en `docs/adr/0001-assetbundles.md`.

### Tarea 1 — Esqueleto del repositorio
- `core/` (.NET Standard 2.1) con el esquema de reglas v0 y su validación, `core.tests/` con xUnit, y build que copie `core.dll` a `unity/Assets/Plugins/`.
- Proyecto Unity 6.6 + URP con carpeta `_Project/`, un `.gitignore` de Unity y `.editorconfig`.
- `docs/rules-schema.md` y `docs/tts-mapping.md` iniciales.

### Tarea 2 — Mesa 3D básica
Cartas, mazos con sprite sheet, fichas y dados con PhysX. Agarrar, soltar, voltear, barajar. Cámara de mesa.

### Tarea 3 — Multijugador
Host autoritativo. Evaluar Netcode for GameObjects vs. FishNet vs. Mirror (compatibilidad con 6.6, Steam transport). Lobby, unirse, sincronizar estado y assets del host.

### Tarea 4 — Formato de reglas + servidor MCP
Motor de reglas en `core/`. Servidor MCP con las herramientas de la sección 3. Meta: que Claude Code arme un juego de cartas sencillo (por ejemplo, un "Guerra" o "UNO simplificado") de principio a fin a partir de un reglamento en texto, preguntando lo ambiguo.

### Tarea 5 — Assets del host
Carga de glTF (glTFast) e imágenes en runtime, caché local, sesiones privadas.

### Tarea 6 — Lua en sandbox
MoonSharp con API restringida. Subconjunto de compatibilidad con las funciones de TTS más usadas.

### Tarea 7 — Personajes con físicas
Ragdoll activo, interacción al hablar.

### Tarea 8 — Catálogo, mods e importador de TTS

## 9. Comandos

```bash
# Core
dotnet build core/
dotnet test core.tests/

# Unity: abrir unity/ con Unity Hub (6.6). Builds con Windows Build Support (Mono).

# Unity CLI (con el Editor abierto)
unity status                 # Editors conectados
unity pipeline list          # debe mostrar "Pipeline: Installed"
unity command                # lista comandos del Editor
unity doctor                 # diagnóstico
```

(Actualizar esta sección cuando existan los proyectos.)
