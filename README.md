# 📚 Legendary Tools Library Overview


This library provides a collection of utilities, data structures, patterns, and editor tools for Unity development. Below is a categorized breakdown of key classes/interfaces, with descriptions of their utility. 🚀


## 🔄 Concurrency and Asynchronous Utilities


- **AsyncWait** 🔄:  
  Provides asynchronous waiting methods (e.g., ForSeconds, ForFrames, Until, While) with support for multiple backends like Unity Coroutines, UniTask, or native Tasks. Useful for non-blocking delays or condition-based waiting in games, reducing coroutine clutter and improving performance in async-heavy code.


- **ThreadedRoutine** 🧵:  
  Manages routines that can run on background threads, with integration for Unity's main thread synchronization. Ideal for offloading CPU-intensive tasks (e.g., computations) without freezing the game loop, while ensuring safe Unity API calls.


## 📊 Data Structures and Algorithms


- **AStar** 🗺️:  
  Implements the A* pathfinding algorithm for grid-based or graph-based navigation. Useful for AI pathfinding in games (e.g., finding shortest paths in maps), with customizable heuristics for efficiency.


- **Bictionary** 🔄:  
  A bidirectional dictionary (two-way mapping between keys and values). Handy for scenarios like entity-ID lookups or reversible mappings, saving time on manual inversion.


- **CircularBuffer** 🔄:  
  A fixed-size buffer that overwrites old data in a circular fashion. Great for logging, history tracking, or fixed-window data (e.g., recent player inputs) without resizing overhead.


- **Tree** 🌳:  
  A generic tree structure for hierarchical data. Useful for scene hierarchies, decision trees, or organizational structures in games.


- **SelfBalanceTree** ⚖️:  
  An AVL-like self-balancing binary search tree. Ensures O(log n) operations for sorted data, ideal for dynamic datasets like leaderboards or sorted inventories.


- **MultiParentTree** 🌿:  
  A tree allowing nodes with multiple parents (Directed Acyclic Graph-like). Useful for complex relationships, such as skill trees with shared prerequisites.


- **Graph** 📈:  
  A generic graph structure with nodes and connections, supporting directed/undirected edges. Core for modeling networks, state machines, or procedural generation (e.g., dungeon layouts).


- **BinaryTree** 🌿:  
  A basic binary tree for ordered data. Suitable for binary search or simple hierarchical storage, like expression trees.


- **Hex** 🔷:  
  Utilities for hexagonal grid systems (e.g., coordinates, neighbors). Essential for hex-based games like strategy titles, handling movement and adjacency efficiently.


- **Inventory** 🎒:  
  A generic inventory system for managing items with stacks, slots, and events. Useful for RPGs or crafting systems to handle item addition/removal without boilerplate.


- **ScriptableObjectInventory** 📜:  
  An inventory backed by ScriptableObjects for data-driven design. Integrates with Unity's asset system for easy editing and persistence of inventories.


- **ManyToManyMap** 🔗:  
  Maps multiple keys to multiple values (e.g., tags to objects). Perfect for tagging systems or relational data without databases.


- **MappedList** 📋:  
  A list with fast lookup via a dictionary map. Combines list ordering with O(1) access, useful for indexed collections like UI elements.


- **MovingAverage** 📉:  
  Computes rolling averages over a window of values. Handy for smoothing data like FPS counters or sensor inputs.


- **NestedType** 🪆:  
  Represents a type with nested subtypes. Useful for reflection-heavy code, like dynamic UI generation from complex types.


- **NestedTypes** 🪆:  
  Collection of nested types for a parent type. Aids in editor tools or runtime type discovery for modular systems.


- **Observable** 👀:  
  Implements the observer pattern for event-driven updates. Useful for decoupling components (e.g., notifying UI of data changes).


- **Octree** 🌐:  
  3D spatial partitioning for efficient queries (e.g., collision detection). Optimizes performance in large 3D scenes by reducing checks.


- **OneToManyMap** 🔗:  
  Maps one key to multiple values. Ideal for grouping (e.g., players by team) with fast lookups.


- **OneToOneMap** 🔗:  
  Strict one-to-one mapping enforcing uniqueness. Useful for pairings like input-action bindings.


- **PriorityQueue** ⏰:  
  A heap-based queue for priority-ordered elements. Essential for task scheduling or AI decision-making.


- **QuadTree** 🟩:  
  2D spatial partitioning for queries (e.g., visibility culling). Improves efficiency in 2D games with many objects.


- **SerializableType** 📦:  
  Serializes System.Type for Unity storage. Allows saving type references in ScriptableObjects or scenes.


- **SerializedDateTime** ⏱️:  
  Serializable wrapper for DateTime. Useful for saving timestamps in player data or logs.


- **SerializedTimeSpan** ⏳:  
  Serializable wrapper for TimeSpan. Handy for durations in configs or save files.


## 🛡️ Design Patterns and Core Utilities


- **Pool** ♻️:  
  Object pooling system for reusing instances (e.g., bullets, enemies). Reduces garbage collection and instantiation overhead in performance-critical games.


- **ServiceLocator** 📍:  
  Global access point for services (e.g., audio manager). Simplifies dependency injection without full DI frameworks.


- **Singleton** 🔒:  
  Ensures a single instance of a class (e.g., game manager). Classic pattern for global state, with Unity-specific handling for persistence.


- **HardStateMachine** ⚙️:  
  A rigid, enum-based state machine for finite states (e.g., player states like idle/jump). Simple and efficient for basic FSM needs.


- **AdvancedStateMachine** ⚙️:  
  Hierarchical state machine with sub-states and transitions. Useful for complex AI or UI flows with nested behaviors.


- **Persistence** 💾:  
  Handles saving/loading data (e.g., player prefs, files). Abstracts storage for cross-platform persistence.


- **SOVariable** 📜:  
  ScriptableObject-based variables for decoupled data (e.g., health). Enables runtime changes and event-driven updates.


- **SOEvent** 📜:  
  ScriptableObject events for broadcasting without direct references. Promotes loose coupling in event systems.


- **ScriptableObjectVariant** 📜:  
  Variants of ScriptableObjects with overrides. Useful for inheritance-like customization without subclassing.


- **Tag** 🏷️:  
  Custom tagging system beyond Unity's built-in tags. Allows multi-tagging for flexible querying.


## 🎮 Unity-Specific Utilities


- **DictionaryConfigEnumWeaver** 🔗:  
  Generates enums from ScriptableObject configs and weaves dictionaries. Automates mapping for data-driven enums (e.g., items).


- **DictionaryConfigNamesWeaver** 🔗:  
  Weaves string constants from config names. Ensures compile-time safety for referencing configs.


- **DebugFilterConfig** 🐞:  
  Configures debug logging levels per type. Filters logs to reduce noise in large projects.


- **Debugger** 🐞:  
  Custom logging with filtering and formatting. Enhances Unity's Debug with type-based control.


- **FollowTransform** 👣:  
  Smoothly follows a target's position/rotation. Useful for cameras or UI elements tracking objects.


- **ProximityDetector** 📡:  
  Detects overlapping actors via triggers/colliders. Base for proximity-based interactions (e.g., NPC detection).


- **VisibilityDetector** 👁️:  
  Checks if objects are visible in the viewport. Optimizes rendering or logic for on-screen elements.


- **UniqueBehaviour** 🔒:  
  Ensures unique MonoBehaviours in scenes (e.g., singletons). Validates duplicates in editor/playmode.


- **UniqueScriptableObject** 📜:  
  Ensures unique ScriptableObjects across assets. Prevents ID conflicts in data-driven systems.


- **UnityHub** 🌐:  
  Central hub for Unity events (Update, FixedUpdate). Simplifies global event subscription.


- **ColorUtil** 🎨:  
  Color manipulation helpers (e.g., lerp, conversions). Useful for procedural colors or UI themes.


- **CurveUtil** 📈:  
  AnimationCurve utilities (e.g., evaluation, editing). Aids in tweening or procedural animations.


- **FlagUtil** 🚩:  
  Bitwise flag operations for enums. Simplifies managing flag-based states (e.g., permissions).


- **HSV** 🎨:  
  HSV color model helpers. Easier for color adjustments than RGB (e.g., hue shifts).


- **MathUtil** ➗:  
  Extended math functions (e.g., clamping, remapping). Fills gaps in Unity's Mathf for common ops.


- **MeshUtil** 🕸️:  
  Mesh generation/manipulation tools. Useful for procedural meshes (e.g., dynamic terrain).


- **Security** 🔐:  
  Basic encryption/decryption for data. Protects save files or assets from tampering.


## 🛠️ Editor Tools and Windows


- **AssetGuidMapper** 🗂️:  
  Maps and tracks GUIDs in project files. Helps with asset refactoring or merge conflicts.


- **AssetNavigatorWindow** 🧭:  
  Editor window for browsing/searching assets. Speeds up asset management in large projects.


- **AssetUsageFinder** 🔍:  
  Finds usages of assets in scenes/prefabs. Essential for cleanup or dependency analysis.


- **CommandGenerator** ⚡:  
  Generates command classes/patterns. Automates undoable actions or input handling.


- **CopySerializedValuesWindow** 📋:  
  Copies serialized data between objects. Useful for duplicating component setups.


- **MonoBehaviourToScriptableObjectConverter** 🔄:  
  Converts MonoBehaviours to ScriptableObjects. Migrates behavior to data-driven assets.


- **UIComponentFieldGenerator** 🖼️:  
  Auto-generates fields for UI components. Saves time in UI scripting.


- **ScriptInSceneAnalyzer** 🔍:  
  Analyzes scripts used in scenes. Helps identify unused code or dependencies.


- **DefineSymbolsEditor** ⚙️:  
  Manages scripting define symbols. Toggles features/platforms in editor.


- **GUIStyleBrowser** 🎨:  
  Browses and previews GUIStyles. Aids in custom editor UI design.


- **NestedTypesEditor** 🪆:  
  Editor for handling nested types. Simplifies inspection of complex data.


- **VisualGraphEditorWindow** 📊:  
  Visual editor for graphs (nodes/edges). Useful for designing state machines or dialogs.


- **PlayerPrefsEditor** 💾:  
  Editor for viewing/editing PlayerPrefs. Debugs persistent data easily.


- **PlayModeStarterFromScene0** ▶️:  
  Starts playmode from scene 0. Automates testing workflows.


- **InlineEditorDrawer** 🖼️:  
  Draws inline editors for properties. Enhances inspector usability.


- **MinMaxSliderDrawer** 📏:  
  Custom drawer for min-max sliders. Improves range editing in inspectors.


- **MultiLevelEnumDrawer** 📊:  
  Drawer for nested/multi-level enums. Handles complex enum hierarchies.


- **SerializableDictionaryDrawer** 📖:  
  Inspector drawer for serializable dictionaries. Makes dicts editable in Unity.


- **SerializableTypeDrawer** 📦:  
  Drawer for SerializableType. Visualizes type references.


- **SerializedDateTimeDrawer** ⏱️:  
  Drawer for SerializedDateTime. User-friendly date editing.


- **SerializedTimeSpanDrawer** ⏳:  
  Drawer for SerializedTimeSpan. Edits durations intuitively.


- **UniqueBehaviourReferenceDrawer** 🔒:  
  Drawer for referencing UniqueBehaviours. Ensures unique selections.


- **MultiScriptableObjectEditor** 📜:  
  Edits multiple ScriptableObjects at once. Batch editing for configs.


- **ScriptableObjectBrowser** 🔍:  
  Browses ScriptableObjects in editor. Quick access to assets.


- **SpreadsheetImporterWindow** 📑:  
  Imports data from spreadsheets (e.g., CSV/Excel). Data-driven content population.


- **RenderingPerformanceHubWindow** 📊:  
  Monitors rendering performance. Profiles draw calls, batches, etc.


- **StatePersisterEditor** 💾:  
  Persists state machine data in editor. Saves/loads FSM configurations.


- **FieldSyncEditor** 🔄:  
  Syncs fields between objects in editor. Automates data alignment.


- **SceneUiObjectsTagger** 🏷️:  
  Tags UI objects in scenes. Organizes canvas elements for querying.


## 🎭 Actor System


- **Actor** 🎭:  
  Base for actor-model entities (message-passing concurrency). Decouples systems for scalable, thread-safe logic.


- **ActorMonoBehaviour** 🎭:  
  MonoBehaviour wrapper for Actors. Integrates actor pattern with Unity's component system.


## ⚙️ Attribute System


- **AttributeConfig** ⚙️:  
  Config for attributes (e.g., health, speed). Data-driven entity stats.


- **Attribute** ⚙️:  
  Runtime attribute with modifiers. Handles buffs/debuffs for RPG entities.


- **Entity** 🧑‍🤝‍🧑:  
  Base entity with attributes. Core for character/NPC systems.

##  🎵 Bragi Audio System


- **Bragi** 📜: High-level audio hub with pooling that spawns AudioHandlers and plays AudioConfig or AudioGroup at a position/parent, including simultaneous, sequential, or chained modes.

- **AudioHandler** 🔊: Component that owns an AudioSource, applies AudioSettings, exposes IsPlaying/IsPaused/IsMuted, fade in/out, events (OnPlay, OnFinished, OnStop), and returns itself to the pool on dispose.

- **Jukebox (+ JukeboxConfig)** 🎶: Playlist player with sequential/random/random-reseeding and loop/circular options; commands (Next/Prev/Mute/Unmute/Pause/Stop) and automatic continuation when a handler finishes.

- **UIAudioTrigger** 🔊: Drop-in component to bind UI/Unity events (pointer, select, drag, lifecycle, or custom string) to AudioConfig plays, with an option to prevent parallel plays.

## 🔧 Miscellaneous Systems

- **Chronos** ⏳:  
  Time management system (e.g., timers, scheduling). Useful for cooldowns or timed events.


## 🎼 Maestro System (Initialization and Task Management)


- **GameInitialization** 🚀:  
  Manages game startup with sequenced init steps. Ensures orderly loading (e.g., assets before UI).


- **InitStepConfig** ⚙️:  
  Configurable init task with dependencies/timeouts. Modularizes startup logic.


- **Maestro** 🎼:  
  Orchestrates tasks with dependencies, timeouts, and internet checks. Handles async initialization graphs.


## 🖥️ Screen Flow System (UI Navigation)


- **ScreenConfig** 🖼️:  
  Config for screens/popups with transitions. Defines navigation rules.


- **ScreenFlow** 🔄:  
  Manages screen transitions, history, and popups. Simplifies app-like navigation in Unity.


## 🖼️ UI Components


- **CircularScrollView** 🔄:  
  Infinite circular scrolling UI. Ideal for carousels or looping lists.


- **DynamicScrollView** 📜:  
  Scroll view with dynamic item population. Optimizes large lists (e.g., inventories).


- **FieldSync** 🔄:  
  Syncs fields between components/objects. Automates data binding.


- **GameObjectListing** 📋:  
  Lists GameObjects (e.g., in UI). Useful for dynamic menus or debug panels.


- **ProximityUiBehaviour** 📡:  
  Displays UI based on proximity (e.g., interaction prompts). Enhances immersive interactions.


- **UIFollowTransform** 👣:  
  UI element follows a 3D transform. For world-space UI like health bars.


- **UIGradient** 🌈:  
  Applies gradients to UI elements. Enhances visual appeal without shaders.


- **UILineConnector** 🔗:  
  Draws lines between UI points. Useful for graphs or connections in menus.


- **UISafeArea** 📱:  
  Adjusts UI for device safe areas (e.g., notches). Ensures compatibility on mobile.
