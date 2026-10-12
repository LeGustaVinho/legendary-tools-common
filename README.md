# 📚 Legendary Tools Library Overview

This library provides utilities, data structures, design patterns, runtime systems and editor tools for Unity development. Below is a categorized overview of the current features, with short descriptions and common uses. Related interfaces, configuration assets, generated overloads and supporting editors are grouped with the feature they serve. 🚀

📖 **Documentation:** [Wiki](https://github.com/LeGustaVinho/legendary-tools-common/wiki) · [Feature Catalog](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Feature-Catalog) · [Feature Browser](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Feature-Browser) · [Common Combinations](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations)

Package metadata: **Legendary Tools 1.3.10**, with **Unity 2022.3** declared in [package.json](package.json). Graph, tree, state-machine and attribute-system generations coexist where noted.

The package declares Burst, Collections, Unity Test Framework, Newtonsoft.Json, Addressables, Editor Coroutines and uGUI dependencies. UniTask and Odin/Nido integrations depend on their packages and scripting symbols.


## 🔄 [Concurrency and Asynchronous Utilities](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Async-and-Startup)

- **[AsyncWait](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AsyncWait)** 🔄: \
  Provides cancellation-aware time/frame/condition waits, coroutine bridges, task combinators and Unity async-operation waits using coroutine, native Task or optional UniTask backends. Coordinate loading, delays and parallel or sequential work. Addressables handle adapters are included; the UniTask backend requires ENABLE_UNITASK.

- **[ThreadedRoutine](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ThreadedRoutine)** 🧵: \
  Runs IEnumerator work on a background thread and switches to Unity execution through explicit yield markers. Move CPU work off the game loop and return to Unity for object access.

- **[ActionExtension (Main-Thread Dispatch)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Main-Thread-Actions)** 🔄: \
  Action extension overloads enqueue callbacks through UnityHub for main-thread execution. Apply background results to Unity objects.

- **[InternetProviderChecker and Implementations](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Internet-Connection-Checks)** 🔄: \
  Replaceable ScriptableObject connectivity checks using Unity reachability, HTTP or ping. Gate online startup steps and choose offline behavior.

## 📊 [Data Structures and Algorithms](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Collections-and-Algorithms)

- **[Bictionary](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Bictionary)** 🔄: \
  Maintains paired key-to-value and value-to-key dictionary lookups. Resolve reversible IDs and object mappings.

- **[OneToOneMap](https://github.com/LeGustaVinho/legendary-tools-common/wiki/OneToOneMap)** 🔗: \
  Manages unique pairings between two sets of keys. Track exclusive assignments and bindings.

- **[OneToManyMap](https://github.com/LeGustaVinho/legendary-tools-common/wiki/OneToManyMap)** 🔗: \
  Stores one key associated with multiple values. Group members by team, owner or category.

- **[ManyToManyMap](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ManyToManyMap)** 🔗: \
  Stores relations where both sides can have multiple associations. Build tag indexes and membership graphs.

- **[MappedList](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MappedList)** 📋: \
  Combines ordered list access with a lookup map. Manage ordered records that also need fast lookup.

- **[CircularBuffer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CircularBuffer)** 🔄: \
  Keeps a bounded sequence in reusable circular storage. Store recent inputs, samples or log history.

- **[PriorityQueue (General and AStar Variants)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Priority-Queues)** 🗺️: \
  Provides priority-ordered queues, including the specialized queue used by AStar. Select the next search node or scheduled item.

- **[MovingAverage](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MovingAverage)** 📉: \
  Calculates a rolling average of recent numeric samples. Smooth FPS, telemetry and noisy measurements.

- **[SerializableDictionary / SerializableDictionaryDrawer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/SerializableDictionary)** 📖: \
  Stores dictionary entries in a Unity-serializable form with a custom Inspector drawer. Author lookup tables in assets and components.

- **[SerializableType / SerializableTypeDrawer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/SerializableType)** 📦: \
  Persists CLR type references and supplies a filtered type picker. Configure factories and select implementations in the Inspector.

- **[SerializedDateTime / SerializedDateTimeDrawer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/SerializedDateTime)** ⏱️: \
  Wraps DateTime for Unity serialization and Inspector date editing. Store timestamps in configs and saved state.

- **[SerializedTimeSpan / SerializedTimeSpanDrawer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/SerializedTimeSpan)** ⏳: \
  Wraps TimeSpan for Unity serialization and Inspector duration editing. Configure cooldown durations and offline intervals.

- **[ScriptableObjectContainer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObjectContainer)** 📊: \
  Returns cloned data from a ScriptableObject container and sets the clone's parent reference. Keep editable templates separate from mutable runtime data.

- **[LinkedGraph / HierarchicalGraph (V1)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Graphs-V1)** 📊: \
  Linked and hierarchical graph structures represent connected nodes and graph nesting. Maintain existing Graph namespace consumers and nested topology.

- **[Tree / Branch (V1)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Tree-V1)** 🌳: \
  Branch-based tree structure in the original graph API. Represent hierarchies used by existing V1 code.

- **[Graph / Node / NodeConnection (V2)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Graphs-V2)** 📈: \
  Interface-based nodes and connections with a reusable graph implementation. Model linked gameplay data and state transitions.

- **[Tree / TreeNode (V2)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Tree-V2)** 🌳: \
  Parent-child tree nodes expose a reusable hierarchy API. Represent scene-independent ownership and menu hierarchies.

- **[BinaryTree](https://github.com/LeGustaVinho/legendary-tools-common/wiki/BinaryTree)** 🌿: \
  Two-child node structure with binary-tree operations. Organize binary hierarchy and search-oriented data.

- **[SelfBalanceTree](https://github.com/LeGustaVinho/legendary-tools-common/wiki/SelfBalanceTree)** ⚖️: \
  Self-balancing binary-search tree with rotation-based rebalancing. Keep mutable ordered data balanced.

- **[MultiParentTree](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MultiParentTree)** 🌿: \
  Hierarchy whose nodes may belong to multiple parents. Model shared prerequisites and overlapping ownership.

- **[NestedType / NestedTypes / NestedTypesEditor](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Nested-Types)** 🪆: \
  Authors ScriptableObject taxonomies with multiple parents, cached hierarchy queries, cycle checks and a dedicated editor. Author item categories, skill classifications and compatible type groups.

- **[AStar](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AStar-Pathfinding)** 🗺️: \
  Generic A* search uses a supplied neighbor and heuristic contract. Find routes on application-defined grids or graphs.

- **[Inventory](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Inventory)** 🎒: \
  Stores generic items with float quantities and supports addition, removal, lookup, transfer and change events. Track resources, quantities and stack-like item counts.

- **[ScriptableObjectInventory](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObjectInventory)** 📜: \
  Inventory wrapper exposes quantity-based storage through a ScriptableObject asset. Share authored inventory data or templates through Unity assets.

- **[Cargo and Containers](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Cargo-and-Containers)** 📊: \
  Capacity-limited cargo groups inventory containers and transfers available quantities between cargos or inventories. Model carrying capacity, shipments and resource transport.

## 🌐 [Spatial Queries, Global Coordinates and Procedural Geometry](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Spatial-and-Geometry)

- **[Hex / FractionalHex / HexMap / Layout](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Hex-Grid)** 🔷: \
  Hex and fractional coordinates, orientation, layout, map and map-view helpers. Build hex-board positioning, neighbor queries and coordinate conversion.

- **[Octree](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Octree)** 🌐: \
  Partitions three-dimensional space into a tree for spatial queries. Narrow searches over objects in 3D worlds.

- **[Quadtree (QuadTree.cs)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/QuadTree)** 🟩: \
  Partitions two-dimensional space into a tree for spatial queries. Search map regions and 2D object neighborhoods.

- **[HierarchicalSparseSpatialHashGrid](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Hierarchical-Spatial-Hash-Grid)** 🌐: \
  Quantized sparse indexing separates static and dynamic entries and supports filtered spatial queries. Query nearby bodies in 2D planes or 3D volumes.

- **[SpatialHashGridSystem / SpatialHashBody / SpatialHashGridVolume](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Spatial-Hash-Scene-Integration)** 🌐: \
  Bodies, volumes and a system wrapper register scene bounds, stable IDs and dynamic updates. Keep a spatial index synchronized with moving Unity objects.

- **[GlobalPosition / GlobalVector / GlobalBounds / GlobalCoordinateConverter](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Global-Coordinates)** 🌐: \
  Integer global positions, vectors and bounds convert to Unity-local coordinates through a configurable world scale. Represent large-world locations without relying on one floating-point origin.

- **[FloatingOriginService / PlayerCoordinateSystemBehaviour](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Floating-Origin)** 🌐: \
  Recenters a local Unity world around an anchor while preserving its global location. Keep large-world movement near the local origin.

- **[ScreenToWorldInfo](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenToWorldInfo)** 🌐: \
  Casts screen-space positions into 3D or 2D physics and exposes hit position and normal. Implement clicking, placement and world selection.

- **[DynamicCube](https://github.com/LeGustaVinho/legendary-tools-common/wiki/DynamicCube-Voxel-Meshing)** 🌐: \
  Builds voxel grids with internal-face culling, greedy quad merging, tiled UVs and optional mesh colliders. Generate editable block structures and compact voxel meshes.

## 🧮 [Deterministic Computing](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Deterministic-Computing)

Managed and Native APIs share deterministic kernels, explicit allocation policies and canonical state formats. Backend selection and numeric/codec policies are explicit; Native owners must outlive their jobs, snapshots and borrowed views.

### ➗ [Fixed-Point Arithmetic and Random](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Deterministic-Computing)

- **[Fixed32 / UFixed32 / Fixed64 / UFixed64](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Point-Scalars)** ➗: \
  Signed and unsigned 32/64-bit fixed-point numbers share configurable precision, rounding, overflow and special-value policies. Use explicit numeric rules in reproducible simulations. Numeric settings must be initialized and frozen before simulation.

- **[FixedMath](https://github.com/LeGustaVinho/legendary-tools-common/wiki/FixedMath)** ➗: \
  Deterministic fixed-point math exposes arithmetic helpers, roots, trigonometry and other functions with status-returning variants. Evaluate movement and gameplay calculations without float-based math.

- **[FixedVector2 / FixedVector3](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Vectors)** ➗: \
  Fixed64-based 2D/3D vectors provide geometry, normalization, interpolation and movement helpers. Implement reproducible positions, directions and motion.

- **[FixedNetwork / FixedVectorNetwork](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Point-Network-Codecs)** ➗: \
  Scalar and vector codecs support fixed-width, varint and range-based bit-packed payloads with compatibility profiles. Encode deterministic numeric state for network transfer.

- **[FixedRandom](https://github.com/LeGustaVinho/legendary-tools-common/wiki/FixedRandom)** ➗: \
  Philox-based deterministic random streams expose ranges, chances, dice, sampling, shuffling and state capture/restore. Reproduce loot, combat rolls and procedural choices across replay and rollback.

- **[FixedRandomWeights](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Weighted-Selection)** ➗: \
  Builds fixed-point weight tables and chooses weighted indexes using FixedRandom. Make reproducible loot tables and weighted decisions.

- **[FixedRandom Geometry](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Spatial-Random)** ➗: \
  Samples fixed-point points inside or on circles, spheres, boxes, rectangles and triangles. Generate reproducible spawn positions.

- **[FixedRandomNetwork](https://github.com/LeGustaVinho/legendary-tools-common/wiki/FixedRandom-Network-State)** ➗: \
  Encodes random state and negotiates algorithm, sampler and numeric compatibility descriptors. Restore matching RNG streams after reconnect or rollback.

- **[FixedBatch / FixedRandomNative / Fixed-Vector Unity Adapters](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Point-Unity-Integration)** ➗: \
  Managed/Burst batch operations, Native random APIs, vector conversion and serialized vector wrappers bridge fixed-point code to Unity. Process native buffers and present deterministic values in Unity.

Further details: [Fixed-point guide](Core/DataStructure/Deterministic/FixedPoint/README.md) · [Vector API](Core/DataStructure/Deterministic/FixedPoint/VECTORS.md) · [Random guide](Core/DataStructure/Deterministic/FixedPoint/Random/README.md).

### 🗃️ [Deterministic Collections, Graphs and Checkpoints](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Deterministic-Computing)

- **[Deterministic Arrays, Cells, Lists, Queues, Deques, Stacks and Ring Buffers](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Sequence-Collections)** 🗃️: \
  Managed and Native arrays, cells, lists, queues, deques, stacks and ring buffers expose defined order and allocation policies. Keep bounded simulation data reproducible across backends. Native equivalents expose the same collection families over native storage.

- **[DeterministicDictionary / DeterministicHashSet](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Hash-Collections)** 🗃️: \
  Dictionary and HashSet variants preserve insertion order with explicit deterministic equality/hash policies. Store simulation lookups with reproducible iteration.

- **[DeterministicSortedList / DeterministicSortedDictionary / DeterministicSortedSet](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Sorted-Collections)** 🗃️: \
  SortedList uses ordered arrays; SortedDictionary and SortedSet use indexed AVL storage, with Managed and Native variants. Maintain stable sorted simulation data. Native variants are available for all three collection families.

- **[DeterministicPriorityQueue](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Priority-Queue)** 🗃️: \
  Minimum-priority queue preserves FIFO order for equal priorities and snapshots its ordering counter. Schedule simulation work with stable ties.

- **[DeterministicBitSet](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-BitSet)** 🗃️: \
  Indexed bit storage supports counting and finding active bits in both memory backends. Track compact simulation masks and membership.

- **[DeterministicSlotMap](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-SlotMap)** 🗃️: \
  Generational handles identify pooled slots with defined reuse and snapshot behavior. Store simulation objects behind stable local handles.

- **[DeterministicSparseSet](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-SparseSet)** 🗃️: \
  Dense component storage uses explicit swap-removal and offers entity-ID ordering. Build application-owned ECS-style simulation storage.

- **[DeterministicLinkedList](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-LinkedList)** 🗃️: \
  Pool-backed linked nodes use generational handles and preserve their links in snapshots. Maintain ordered simulation chains without CLR object nodes.

- **[DeterministicMultiMap](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-MultiMap)** 🗃️: \
  Keys and each key's values retain insertion order through shared Managed/Native algorithms. Store reproducible one-to-many simulation relations.

- **[DeterministicGraph / NativeDeterministicGraph](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Graphs)** 🗃️: \
  Mutable Managed and Native graph storage uses stable graph handles and deterministic topology operations. Represent simulation networks and dependency relationships.

- **[FrozenDeterministicGraph / NativeFrozenDeterministicGraph](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Frozen-Deterministic-Graphs)** 🗃️: \
  Frozen graph variants and canonical bindings provide immutable graph topology for queries and transport. Publish a graph for repeated read operations.

- **[PersistentDeterministicGraph / NativePersistentDeterministicGraph](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Graphs#persistent-graph-versions)** 🗃️: \
  Publishes immutable graph versions from an exclusive copy-on-write workspace, with version leases, configurable retention and batch mutation APIs. Keep previous graph states available while deriving and selecting new versions.

- **[Graph Delta Transfers](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Graphs#graph-delta-transfers)** 📈: \
  Writes, validates and applies forward or reversible graph transfers, choosing a patch or full checkpoint based on encoded size. Transfer graph changes with baseline/target hash validation and explicit codecs.

- **[Deterministic Graph Algorithms](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Graph-Algorithms)** 📈: \
  Reachability, breadth/depth-first traversal, cycle detection, topological order and component analysis use explicit scratch/output storage. Analyze deterministic simulation graphs.

- **[Collection Snapshots and Buffer Policies](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Collection-Snapshots)** 🗃️: \
  Bounded snapshot slots and leases capture/restore collections with full-copy or copy-on-write storage. Rewind local collection state with explicit memory limits. Collection-level contiguous snapshots are full copies; world-level keyframe/delta retention is implemented separately by DeterministicWorldHistory.

- **[Canonical Checkpoints, Field Codecs and Hashing](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Canonical-Checkpoints-and-Codecs)** 🗃️: \
  Versioned binary readers/writers, field codecs and canonical collection bindings preserve logical state and validate data before import. Compare hashes and exchange reproducible snapshots across backends.

- **[Fixed-Point Policies / FixedRandomStateCodec / Compatibility Profiles](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Point-Deterministic-Adapters)** 🗃️: \
  Fixed numeric policies, RNG state codecs and compatibility fingerprints integrate FixedPoint with deterministic containers/runtime. Register numeric and RNG semantics for rollback and session compatibility.

Further details: [Collection guide](Core/DataStructure/Deterministic/Collections/README.md).

### 🎞️ [Simulation, Replay and Network Sessions](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Deterministic-Computing)

- **[DeterministicRuntimeContext](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Runtime-Context)** 🎞️: \
  Selects an explicit backend and a compatible memory factory for host-side orchestration. Configure Managed or Native/Burst simulation storage.

- **[DeterministicStateRegistry](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-State-Registry)** 🎞️: \
  Registers state by stable IDs, freezes compatibility signatures and captures/restores canonical world checkpoints. Define the complete state that a simulation can rewind or exchange.

- **[DeterministicWorldHistory](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-World-History)** 🎞️: \
  Retains bounded world boundaries as full checkpoints, keyframes/deltas or shared canonical pages. Trade snapshot memory against rollback retention.

- **[DeterministicInputHistory](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Input-History)** 🎞️: \
  Tracks immutable real inputs, predicted inputs and historical player participation by tick. Detect prediction mismatches and resimulate affected ticks.

- **[DeterministicCommandBuffer / NativeDeterministicCommandBuffer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Command-Buffers)** 🎞️: \
  Producer partitions merge commands in a defined total order; the Native variant supplies job writers and dependency checks. Combine parallel simulation work without scheduling-dependent command order.

- **[DeterministicSimulationRunner](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Lockstep-and-Rollback-Simulation)** 🎞️: \
  A tick runner advances lockstep or predicted simulation, restores checkpoints, resimulates and confirms progress. Build deterministic gameplay driven by normalized inputs. Application code supplies the simulation step; host orchestration is Managed, while Native kernels can execute in Burst.

- **[DeterministicEventLedger](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Event-Ledger)** 🎞️: \
  Tracks speculative events, retractions and once-only confirmed delivery. Present rollback-safe sound, VFX and gameplay notifications.

- **[DeterministicReplayArchive](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Replay-Archive)** 🎞️: \
  Stores real inputs, hashes and versioned checkpoints with restoration and seeking. Record matches, reproduce bugs and inspect simulation history.

- **[DeterministicNetworkSession](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Network-Sessions)** 🎞️: \
  Coordinates inputs, commits, late join, reconnect and migration around the simulation runtime. Build authoritative-host or P2P deterministic sessions. Real network delivery is supplied through an application IDeterministicTransport adapter.

- **[DeterministicRaftNode](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Raft-Coordination)** 🎞️: \
  Elections, replicated logs, snapshots and joint membership changes coordinate session consensus. Support quorum-based commits and host migration.

- **[IDeterministicTransport / DeterministicLoopbackNetwork](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Transport-and-Loopback-Network)** 🎞️: \
  Datagram contracts and a loopback network simulate delivery, loss, duplication and delay. Connect a transport adapter and test network-session behavior.

Further details: [Runtime guide](Core/DataStructure/Deterministic/Runtime/README.md).

## 🛡️ [Design Patterns and Core Utilities](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Architecture-and-State)

- **[Pool / PoolObject / PoolGameObject](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Object-Pooling)** ♻️: \
  Pools plain objects and Unity GameObjects through poolable contracts and reusable instance APIs. Reuse projectiles, actors, audio handlers and UI items.

- **[ServiceLocator](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ServiceLocator)** 📍: \
  Registers services by type and exposes shared service lookup. Resolve application services from loosely connected consumers.

- **[Singleton / SingletonBehaviour / SingletonScriptableObject](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Singletons)** 🔒: \
  Generic, MonoBehaviour and ScriptableObject singleton bases provide shared-instance patterns and related attributes. Centralize services with a suitable Unity lifecycle.

- **[MVCManager / BaseView / BaseController / IModel](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MVC-Framework)** 🛡️: \
  Model contracts and view/controller bases pair through a manager and model factories. Separate view lifecycle from application data and control logic.

- **[Observable and Typed Wrappers / AccessorObservable](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Observable-Values)** 👀: \
  Generic and typed values raise old/new change notifications; AccessorObservable wraps external getters/setters. Expose state changes to UI and other consumers.

- **[ObservableList / ObservableListDrawer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ObservableList)** 🛡️: \
  List operations notify additions, updates, removals and clearing, including affected indexes. React to mutable collections in UI and gameplay.

- **[ObservableDictionary / ObservableDictionaryDrawer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ObservableDictionary)** 🛡️: \
  Dictionary operations publish entry-change notifications and support Inspector editing. Observe keyed state and configuration changes.

- **[StateMachine (V1)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/StateMachine-V1)** 🛡️: \
  Original linked-graph state machine supports triggers, transition arguments and recorded state history. Maintain trigger-driven flows in the original state-machine namespace.

- **[HardStateMachine (V2)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/HardStateMachine-V2)** ⚙️: \
  Enum-backed states expose entry/update/exit callbacks and optional transition permission checks. Control compact gameplay or UI state sets.

- **[AdvancedStateMachine (V2)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AdvancedStateMachine-V2)** ⚙️: \
  Uses graph-based states, typed parameters, any-state transitions and composable boolean, numeric and trigger conditions. Configure parameter-driven gameplay and navigation transitions.

- **[ICommand / IAsyncCommand](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Command-Contracts)** 🛡️: \
  ICommand exposes Execute/Unexecute on a receiver; IAsyncCommand provides Task-based asynchronous execution. Represent reversible or asynchronous actions through common interfaces.

- **[UnityBehaviour / UnityObject / UnityBehaviourEditor](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Unity-Serialization-Bases)** 🛡️: \
  UnityBehaviour and UnityObject select standard Unity bases or Odin serialized bases through a compile symbol. Share a consistent serialization foundation for custom components and assets.

## 📜 [ScriptableObject Architecture](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-ScriptableObject-Architecture)

- **[SOVariable and Typed Variables](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-Variables)** 📜: \
  Typed variable assets expose mutable values, change events and lifetime scopes. Share state across scenes and components through assets.

- **[SOReference and Typed References](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-References)** 📜: \
  Typed wrappers select a local literal value or a variable asset. Author reusable components with configurable shared or local data.

- **[SORangedNumber and Ranged Variables](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Ranged-ScriptableObject-Variables)** 📜: \
  Number-variable assets add configured limits for int, float and double values. Constrain authored or runtime numeric state.

- **[VariableListener and Typed Listeners](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-Variable-Listeners)** 📜: \
  Generic and typed listener components forward variable changes into UnityEvents. Connect asset state to scene behavior in the Inspector.

- **[SOEvent (Zero to Six Parameters)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-Events)** 📜: \
  Event-channel assets broadcast without direct scene references, with zero to six typed parameters. Decouple gameplay producers from scene listeners.

- **[ScriptableObjectVariant](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-Variants)** 📜: \
  Base assets and property-path override sets resolve customized ScriptableObject values. Author variants of content configs without a new class for each variant.

- **[PersistenceProviderSO / PlayerPrefsPersistenceSO](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-Variable-Persistence)** 📜: \
  Provider assets persist variables through a replaceable abstraction with a PlayerPrefs implementation. Save simple asset-based settings and variable values.

- **[SubAssetAttribute and Sub-Asset Tools](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-Sub-Assets)** 📜: \
  SubAsset attributes, drawers and postprocessing manage nested ScriptableObject assets. Keep related authored objects inside a parent asset.

## 🔗 [Reactive Bindings](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Binding-and-Scripting)

- **[Reactive Component Bindings](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reactive-Component-Bindings)** 🔗: \
  Observable-to-component bindings cover TMP text/input/dropdown, sliders, toggles, images, scrollbars, CanvasGroups and transforms. Update HUD and settings controls from observable values.

- **[Fluent Reactive Binding Builders](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fluent-Reactive-Bindings)** 🔗: \
  Builder APIs configure component bindings with direction, formatting and control-specific options. Compose readable binding setup in C#.

- **[BindingHandle / BindingAnchor / BindingDisposer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reactive-Binding-Lifetime)** 🔗: \
  Disposable handles, anchors and update drivers provide suspend/resume, resync and automatic owner-lifecycle cleanup. Keep subscriptions correct when UI is disabled, pooled or destroyed.

- **[BindingsDebuggerWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reactive-Bindings-Debugger)** 🔗: \
  Editor window inspects registered live bindings and their diagnostic metadata. Trace observable-to-UI binding behavior.

## 🔗 [ViewDataBinder (Declarative Data Binding)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Binding-and-Scripting)

- **[ViewDataBinder](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ViewDataBinder)** 🔗: \
  Inspector or runtime-authored member bindings synchronize one-way, reverse or two-way with selectable update timing. Bind plain objects, components, static members or provided models to views. Two-way bindings require reversible converters; reflected members may need preservation in stripped builds.

- **[ViewDataEventBinder](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ViewDataEventBinder)** 🔗: \
  Polls source changes, evaluates AND/OR comparison clauses and invokes UnityEvents or public Task-returning methods. Trigger behavior when model conditions change. Task actions accept public non-generic Task-returning methods, and cancellation belongs to the invoked method.

- **[ViewDataListBinder](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ViewDataListBinder)** 🔗: \
  Projects IEnumerable data into keyed prefab items with filtering, sorting, paging, pooling and optional ScrollRect virtualization. Build inventory lists, tables and grids with per-item data contexts. Virtualization supports vertical, horizontal and uniform-grid layouts and controls content positioning instead of a LayoutGroup.

- **[ViewDataBindingProfile / BindingDataContext / Instance Providers](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Profiles-and-Contexts)** 🔗: \
  Reusable profile assets and named hierarchy contexts resolve model/view roots and support runtime overrides. Reuse a binding definition across multiple prefab instances.

- **[BindingConverter and Built-In Converters](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Converters)** 🔗: \
  Reusable converter assets bridge numeric/string/bool/null/enum values; eligible converters also support reverse conversion. Show typed model values in UI and accept compatible edited input.

- **[CompositeStringBindingFormatter / BindingFormatterRegistry](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Formatters)** 🔗: \
  Composite string formatting combines multiple sources with optional culture settings and an extensible formatter registry. Produce labels such as player name and current/max health. Composite formatter bindings currently run in the SourceToTarget direction.

- **[Binding Conditions, Fallbacks and Recovery Policies](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Conditions-and-Recovery-Policies)** 🔗: \
  Per-binding conditions, conflict/write rules, fallbacks, null handling, missing-endpoint recovery and error policies control synchronization. Handle pooled views, temporary models and invalid values predictably.

- **[BindingBackendRegistry and Execution Plans](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Backends-and-Execution)** 🔗: \
  Replaceable member/source backends and instance resolvers use cached execution plans and a shared update scheduler. Customize access and composition while controlling polling cost.

- **[BindingAccessorCodeGenerator](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Accessor-Code-Generation)** 🔗: \
  Editor/build scanning generates member accessors and eligible typed direct plans, with reflection fallback for unsupported paths. Reduce reflective calls in player builds and IL2CPP binding workloads. Unsupported paths retain a cached reflection fallback. If build-time generation changes code, compile it before restarting the build.

- **[Binding Inspectors, Member Pickers and Runtime Statistics](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Inspector-and-Diagnostics)** 🔗: \
  Member/type pickers, value previews, status indicators, extension slots and runtime counters support binding authoring and diagnosis. Validate paths and inspect synchronization cost or failures.

Further details: [Binding API and Inspector guide](Core/ViewDataBinder/README.md).

## 📝 [Expressions and Runtime Scripting](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Binding-and-Scripting)

- **[ExpressionEngine / CompiledExpression / EvaluationContext](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Generic-Expression-Engine)** 📝: \
  Parses, compiles, caches and evaluates numeric/boolean expressions with arithmetic, comparison, logical, assignment and function nodes. Author formulas and conditional gameplay rules as text.

- **[Expression Variable Providers and Scope Relations](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Expression-Providers-and-Scopes)** 📝: \
  Dictionary/instance providers and relation-aware named scopes resolve expression variables across logical owners. Evaluate self, parent, owner or other contextual values.

- **[MiniCSharpInterpreter](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MiniCSharp-Interpreter)** 📝: \
  Executes a C#-like language with variables, functions, control flow, exceptions and reflection-based object/type interop. Embed configurable scripting over application objects.

- **[MiniCSharpBehaviour / RuntimeScriptAsset](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MiniCSharp-Unity-Hosting)** 📝: \
  A component forwards Unity callbacks to script functions; RuntimeScriptAsset stores source and caches compiled scripts. Attach authored runtime logic to GameObjects and script assets.

- **[MiniCSharp TypeAccessPolicy](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MiniCSharp-Type-Access-Policy)** 📝: \
  Type, name, namespace and assembly blacklist rules gate reflection interop; the interpreter also controls automatic AppDomain type resolution. Configure which host APIs an embedded script can resolve. The default access policy allows types unless a blacklist rule rejects them.

## 💾 [Persistence and Serialization](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Persistence-and-Time)

- **[Persistence / DataTable](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Persistence)** 💾: \
  Typed, ID-based data tables support set/get/remove, version metadata, callbacks and save/load operations with pluggable providers. Store application state and observe saved-record changes.

- **[Serialization Providers and Unity Value Converters](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Persistence-Serialization-Providers)** 💾: \
  Provider assets select JSON, XML or binary serialization, with optional Odin/Nido implementations and Unity converters. Choose the save representation appropriate to stored data.

- **[DiskStorage / IStorable](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Persistence-Disk-Storage)** 💾: \
  Storage abstraction and DiskStorage read/write a configured Unity file path. Keep serialized saves under the chosen Unity root path.

- **[AesEncryptionProvider](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Persistence-AES-Encryption)** 💾: \
  Replaceable AES encryption provider supplies the optional encryption stage in the persistence pipeline. Encrypt serialized save payloads when configured.

- **[GZipUtility / Base64Utility](https://github.com/LeGustaVinho/legendary-tools-common/wiki/GZip-and-Base64-Helpers)** 💾: \
  Compression and Base64 utilities support persistence byte/string transformations. Reduce or text-encode serialized payloads.

- **[FlatDataSerializer / FlatObject / FlatTable](https://github.com/LeGustaVinho/legendary-tools-common/wiki/FlatDataSerializer)** 💾: \
  Flattens public members of nested objects/collections into typed paths and tables, then reconstructs them through configurable factories/converters. Produce flat records for inspection, export or data exchange. Arrays, lists and nested objects are supported; dictionaries and polymorphic type metadata are not supported in this version.

- **[Serialization Helpers](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Serialization-Helpers)** 💾: \
  Standalone XML/binary and optional Odin helpers serialize values and files. Support utility code outside the provider-based save pipeline.

- **[Security (RSA Helpers)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/RSA-Security-Helpers)** 🔐: \
  RSA key import/export and typed, byte and file encryption/decryption helpers wrap the platform cryptography API. Integrate existing RSA-oriented data tools.

- **[UnityFilePath](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UnityFilePath)** 💾: \
  Builds file paths from Unity root locations, subfolders, filename and extension. Configure portable locations for saves and exports.

Further details: [FlatDataSerializer guide](Core/FlatDataSerializer/README.md).

## 📦 [Asset Loading and Identity](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Assets-and-Identity)

- **[AssetLoaderConfig / LoadOperation and Loader Strategies](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Asset-Provider-and-Loaders)** 📦: \
  Loader assets unify hard references, Resources and optional Addressables behind load operations and async loading. Load actor, audio and screen assets through interchangeable strategies.

- **[ResourcePathReference](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ResourcePathReference)** 📦: \
  Serializable Resources-path reference includes an Inspector drawer for asset selection. Configure Resources loading paths without hand-authored strings.

- **[IdentifiedBehaviour / IUnique](https://github.com/LeGustaVinho/legendary-tools-common/wiki/IdentifiedBehaviour)** 📦: \
  Component base exposes generated identity and identity-change behavior. Identify scene objects independently of display names.

- **[UniqueBehaviour / UniqueBehaviourReference / UniqueBehaviourReferenceDrawer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UniqueBehaviour-and-References)** 🔒: \
  Unique components, serialized references and object listings work with editor validation and a reference picker. Resolve identified scene components and catch duplicate identities.

- **[UniqueScriptableObject](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UniqueScriptableObject)** 📜: \
  ScriptableObject base assigns identity to assets and participates in uniqueness tooling. Identify authored data assets beyond their names.

- **[Tag / TagFilter / ITaggable](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Tags-and-Tag-Filters)** 🏷️: \
  Asset-defined tags, a taggable contract and filters model multiple custom classifications. Select gameplay objects by several tags instead of one Unity tag.

## 🎮 [Unity-Specific Utilities](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Unity-Runtime-Helpers)

- **[UnityHub](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UnityHub)** 🌐: \
  Central dispatch exposes Unity loop/lifecycle callbacks and queued main-thread actions. Drive reusable non-MonoBehaviour services from Unity events.

- **[Debugger / DebugFilterConfig](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Filtered-Debugger)** 🐞: \
  Type/category filtering configuration controls custom logging output and formatting. Reduce diagnostic noise and enable targeted logs.

- **[FollowTransform](https://github.com/LeGustaVinho/legendary-tools-common/wiki/FollowTransform)** 👣: \
  Follows position/rotation with offsets, thresholds, smoothing and Update/LateUpdate selection. Attach cameras or companion transforms to a target.

- **[LookAt](https://github.com/LeGustaVinho/legendary-tools-common/wiki/LookAt)** 🎮: \
  Faces a target using selectable axes, axis constraints and optional reverse facing. Orient indicators, billboards and scene objects.

- **[ProximityDetector](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ProximityDetector)** 📡: \
  Generic trigger-based detection tracks compatible nearby components and entry/exit behavior. Detect interaction candidates and nearby entities.

- **[VisibilityDetector](https://github.com/LeGustaVinho/legendary-tools-common/wiki/VisibilityDetector)** 👁️: \
  Renderer visibility callbacks expose visible state and change notifications. Enable behavior when an object becomes visible to a camera. Visibility is based on Renderer callbacks and can be affected by any rendering camera, including editor cameras.

## 🧰 [Math, Colors, Meshes and Extensions](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Math-and-Extensions)

- **[FlagUtil / EnumExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Enum-and-Flag-Utilities)** 🚩: \
  Enum extensions and FlagUtil validate values and test/add/remove enum or numeric flags. Manage capability masks and flag-based configuration.

- **[ColorUtil / HSV](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Color-and-HSV-Utilities)** 🎨: \
  Color interpolation helpers and an HSV value type support RGB/HSV manipulation. Generate palettes and interpolate UI or procedural colors.

- **[CurveUtil](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CurveUtil)** 📈: \
  Evaluates cubic spline interpolation between scalar control values. Smooth procedural values and custom interpolation.

- **[MathUtil](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MathUtil)** ➗: \
  Geometry intersections, random circle points and Fibonacci helpers supplement common math tasks. Solve spatial tests and small numeric utilities.

- **[MeshUtil](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MeshUtil)** 🕸️: \
  Creates polygon/convex/hex meshes, welds vertices, orders points and simplifies shapes. Generate and clean procedural geometry.

- **[CameraUtil](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Camera-Framing)** 🧰: \
  CameraUtil frames a GameObject or bounds for a chosen view. Center previews, inspection cameras and object showcases.

- **[PrintSaver](https://github.com/LeGustaVinho/legendary-tools-common/wiki/PrintSaver-Screenshots)** 🧰: \
  Component saves screenshots with generated identifiers and configured capture behavior. Capture previews and debugging images.

- **[DictionaryExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Dictionary-Extensions)** 🧰: \
  Adds update-or-insert, key/value extraction and weighted random selection helpers. Reduce repetitive dictionary operations.

- **[EnumerableExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Collection-Extensions)** 🧰: \
  Helpers shuffle, swap, rotate, resize, query, clone and modify arrays/lists/sets. Prepare collections and random selections in utility code.

- **[GenericExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deep-Clone-and-Equality)** 🧰: \
  Generic helpers perform deep cloning and deep comparison. Duplicate data templates and compare structured values.

- **[NumericExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Numeric-Extensions)** 🧰: \
  Adds range remapping, approximate comparison and compact float formatting. Convert values for UI and compare approximate measurements.

- **[SortingExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Sorting-Extensions)** 🧰: \
  Provides HeapSort and Timsort over lists with configurable comparers. Sort data using an explicit algorithm/comparer.

- **[StringExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/String-Naming-Extensions)** 🧰: \
  Converts camel-case names to constants and normalizes enum identifiers. Prepare generated symbols and readable configuration names.

- **[TaskExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Task-Extensions)** 🧰: \
  FireAndForget observes task failure handling through a small convenience extension. Launch asynchronous work from callback-oriented code.

- **[TypeExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Type-and-Reflection-Extensions)** 🧰: \
  Caches/discovers CLR types and checks constructors, attributes, type families and Unity serializability. Build factories, pickers and reflection-driven tooling.

- **[UnityExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Unity-Extensions)** 🧰: \
  Component/UI, bounds, animation, snapping, gizmo, prefab/scene and dirty-state helpers extend Unity APIs. Reduce routine scene, UI and editor glue code.

- **[EditorExtensions](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Editor-Asset-Search-Extensions)** 🧰: \
  Finds nearby/config assets, searches by type/name and resolves script asset paths. Implement focused editor pickers and generators.

- **[DateTimeExtension](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Date-and-Time-Extensions)** 🧰: \
  Formats TimeSpan values and checks whether DateTimes share a day. Display countdowns and group time-based events.

## 🛠️ Editor Tools and Windows

### 🗂️ [Asset and Content Tools](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Asset-and-Content-Editors)

- **[AssetGuidMapper](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AssetGuidMapper)** 🗂️: \
  Indexes GUID references in Unity serialized files and maintains forward/reverse lookup data. Inspect asset dependencies and support reference-analysis tools.

- **[AssetNavigatorWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Asset-Navigator)** 🧭: \
  Window navigates recently selected assets with history and search controls. Move quickly between frequently inspected assets.

- **[ReferenceTrackerWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reference-Tracker)** 🗂️: \
  Searches serialized object/asset references within selectable scopes and groups matching usages. Find where a scene object, component or asset is referenced.

- **[MissingReferenceScannerWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Missing-Reference-Scanner)** 🗂️: \
  Scans Unity content for unresolved serialized references and missing scripts. Find broken assets and scene/component references.

- **[OrphanAssetScannerWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Orphan-Asset-Scanner)** 🗂️: \
  Analyzes asset references and candidate roots to report potentially unreferenced assets. Review cleanup candidates before removing project content.

- **[UITextureSizeOptimizerWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UI-Texture-Size-Optimizer)** 🗂️: \
  Scans UI texture usage/layout, estimates useful imported sizes and previews/applies importer changes with report export. Reduce oversized UI texture imports using measured usage.

- **[ScriptableObjectBrowser](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-Browser)** 🔍: \
  Searches and browses ScriptableObject assets by type and other filters. Find authored configs quickly.

- **[MultiScriptableObjectEditor / ScriptableObjectTableView](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Multi-Asset-ScriptableObject-Editor)** 📜: \
  Edits multiple ScriptableObjects in a table with shared fields and resizable rows/columns. Compare and batch-edit content definitions.

- **[SpreadsheetImporterWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Spreadsheet-Importer)** 📑: \
  Maps spreadsheet columns to individual ScriptableObject assets or collections stored inside an asset. Populate authored game data from tabular content. The implemented import format is CSV/tabular content, including Google spreadsheet access, rather than native .xlsx parsing.

- **[UIPaletteWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UI-Sprite-Palette)** 🗂️: \
  Thumbnail palette and drag/drop handlers support browsing and applying UI sprite assets. Style scene UI from a reusable sprite collection.

- **[SceneUiObjectsTagger](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Scene-UI-Objects-Tagger)** 🏷️: \
  Editor action tags eligible UI objects in the scene. Apply a consistent Unity tag to UI hierarchies.

- **[AssetThumbnails / PrefabIconGenerator and Thumbnail Caches](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Asset-and-Prefab-Icons)** 🗂️: \
  Custom icon generation, stores, project drawing and prefab preview caching synchronize asset visuals. Make project assets easier to recognize in editor views.

- **[GameObjectPathMenu / PingObject](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Editor-Object-Navigation)** 🗂️: \
  Context actions copy GameObject hierarchy paths and ping selected objects/assets. Locate objects and share exact hierarchy paths during debugging.

### ⚡ [Code Generation, Analysis and Automation](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Code-and-Automation-Editors)

- **[DictionaryConfigEnumWeaver](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Config-to-Enum-Weaver)** 🔗: \
  Generates enum mappings from configuration listings and runs configured weaving through editor/build hooks. Keep authored config catalogs aligned with generated enum references.

- **[DictionaryConfigNamesWeaver](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Config-Name-Constants-Weaver)** 🔗: \
  Generates names/constant listings from config assets through the shared weaving infrastructure. Reference authored configurations through generated string symbols.

- **[WeaverUtils](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Code-Weaver-Utilities)** ⚡: \
  Writes enum/constant listings and UI binding class files, with property/event mapping helpers. Build small generators around authored names and UI metadata.

- **[CommandGenerator](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Command-Generator)** ⚡: \
  Reflects public methods of a selected class and generates command wrappers, including eligible async methods. Create consistent receiver-based action boilerplate.

- **[UIComponentFieldGenerator](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UI-Component-Field-Generator)** 🖼️: \
  Generates component fields/binding code from a selected UI hierarchy. Reduce manual UI component-reference declarations.

- **[MonoBehaviourToScriptableObjectConverter](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MonoBehaviour-to-ScriptableObject-Converter)** 🔄: \
  Generates ScriptableObject-oriented code from selected MonoBehaviour source and supports the conversion workflow. Move component data toward asset-driven configuration.

- **[CopySerializedValuesWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Copy-Serialized-Values)** 📋: \
  Editor window copies serialized values between compatible objects. Reuse component or asset settings.

- **[ReferenceTransferToolWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reference-Transfer)** ⚡: \
  Reusable mappings transfer object references and component values between source/target hierarchies. Move configured setups to corresponding objects in another hierarchy.

- **[SerializedSnapshotLibrary / SerializedSnapshotWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Serialized-Snapshot-Library)** ⚡: \
  Captures component/GameObject serialized state and object references, previews differences and reapplies stored snapshots. Reuse editor setups and recover captured component state.

- **[ManualCodeControlWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Manual-Code-Control)** ⚡: \
  Monitors external code changes and exposes explicit import, compilation and reload actions when Auto Refresh is disabled. Control when Unity compiles/reloads during editing.

- **[CodeAnalyzerWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Code-Type-Usage-Analyzer)** ⚡: \
  Scans source folders with text/regex analysis and reports declared-type usage per class and globally. Review source-level coupling and type usage.

- **[ScriptInSceneAnalyzer](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Scripts-in-Scene-Analyzer)** 🔍: \
  Reports scripts attached to scene objects and their usage. Inspect scene script dependencies and locate attached behaviors.

- **[AggregateCodeFiles](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Simple-Code-Aggregation)** ⚡: \
  Folder-based editor utility concatenates source files into a single output. Collect source context for sharing or inspection.

- **[CSFilesAggregatorWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CSharp-Files-Aggregator)** ⚡: \
  Selectable file/folder aggregation combines dependency expansion, transforms, diagnostics, persistence and clipboard/output formatting. Prepare focused source bundles for review and AI-assisted work.

- **[TypeIndex / TypeIndexService](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CSharp-Type-Index)** ⚡: \
  Roslyn scanners persist an index of declared types and their source paths with rebuild/settings actions. Resolve type names to files for dependency-aware tooling.

- **[CodeDependencyScanner](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Source-Dependency-Scanner)** ⚡: \
  Roslyn-based analysis resolves type references and walks a filtered dependency graph to a configurable depth. Expand a source selection with the code it depends on.

- **[CSharpImplementationStripper](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CSharp-Implementation-Stripper)** ⚡: \
  Transforms source to reduce implementation bodies using configurable strip options and method-body modes. Create smaller API-focused source context.

- **[DLLDocumentationExporter](https://github.com/LeGustaVinho/legendary-tools-common/wiki/DLL-API-Documentation-Exporter)** ⚡: \
  Reflects compiled assemblies and exports API documentation. Inspect library surfaces when source aggregation is unsuitable.

- **[CsFileTreeWindow (Source Metrics)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CSharp-File-Metrics)** ⚡: \
  Tree view filters source files by size and comment-coverage metrics. Find large or sparsely documented code files.

- **[ListCSFilesEditor](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CSharp-File-Inventory)** ⚡: \
  Editor menu lists project C# source files. Produce a quick code inventory.

- **[AICodePasteHubWindow (AI Clipboard Pipeline)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AI-Clipboard-Pipeline)** ⚡: \
  Classifies clipboard C# or patches, resolves target paths, applies changes and tracks history/undo with compile gates. Review and apply externally prepared code changes inside Unity.

- **[AiFriendlyCompilationErrorReporter](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Compilation-Error-Reports)** ⚡: \
  Collects compiler errors into consolidated AI-friendly reports and records compilation outcome markers. Feed precise Unity compile failures into code-fixing workflows.

- **[RoslynNuGetInstaller](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Roslyn-Installer)** ⚡: \
  Installs/reconciles or removes the LegendaryTools-managed Roslyn dependency copy. Prepare dependencies for type indexing and source analysis.

### 🎨 [Inspector, Workspace and Performance Tools](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Editor-Interface-and-Diagnostics)

- **[Inspector Attributes and Property Drawers](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Inspector-Attributes-and-Drawers)** 🎨: \
  Attributes and drawers add method buttons/order, inline editors, min/max ranges, multilevel enums and observable-value editing. Make custom components and assets easier to configure. Includes ButtonAttribute, PropertyOrderAttribute, InlineEditorDrawer, MinMaxSliderDrawer, MultiLevelEnumDrawer and observable-value/collection drawers.

- **[DefineSymbolsEditor](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Define-Symbols-Editor)** ⚙️: \
  Editor window manages scripting define symbols by build target/configuration. Toggle optional library integrations and project compilation flags.

- **[GUIStyleBrowser](https://github.com/LeGustaVinho/legendary-tools-common/wiki/GUIStyle-Browser)** 🎨: \
  Browses and previews available GUIStyle definitions. Choose styles for custom IMGUI editor windows.

- **[PlayModeStarterFromScene0](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Play-Mode-from-Scene-Zero)** ▶️: \
  Editor hook starts Play Mode from the first configured build scene. Test the project's complete startup flow from any edited scene.

- **[Hierarchy Decorator](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Hierarchy-Decorator)** 🎨: \
  Hierarchy drawers add configurable component icons, category/branch styling, breadcrumbs, toggles and tag/layer information. Make complex scene hierarchies easier to navigate.

- **[VisualGraphEditorWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Visual-Graph-Node-Editor)** 📊: \
  Reusable graph editor provides node/edge models, canvas pan/grid/selection/resize, inspectors, clipboard, menus and toolbar extensions. Build custom visual authoring tools and dependency graphs.

- **[PlayerPrefsEditor / PlayerPrefsTools](https://github.com/LeGustaVinho/legendary-tools-common/wiki/PlayerPrefs-Editor)** 💾: \
  Tools inspect and edit PlayerPrefs entries. Debug simple saved settings and SOAP persistence.

- **[RenderingPerformanceHubWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Rendering-Performance-Hub)** 📊: \
  Collects rendering configuration and diagnostic information in an editor dashboard, with shortcuts to related settings. Review rendering configuration before targeted optimization.

- **[RendererAuditWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Renderer-Audit)** 🎨: \
  Scans scene renderers and groups resource/cost estimates around batching, instancing, shadows and transparency. Identify rendering optimization candidates with scene context.

- **[DomainReloadAnalyzerWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Domain-Reload-Analyzer)** 🎨: \
  Captures reload logs/profiler markers, phase timelines and project findings with per-script/assembly attribution where available. Investigate slow Unity reloads and startup hooks.

Further details: [Domain reload analysis guide](Editor/Analysis/DomainReload/README.md).

## 🎭 [Actor System](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Gameplay-and-Attributes)

- **[Actor / ActorMonoBehaviour](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Actor-Lifecycle-System)** 🎭: \
  Separates plain C# entity logic from a MonoBehaviour body, with possession/ejection, GameObject creation/destruction and actor registration by type. Separate entity logic from its Unity scene representation.

- **[ConfigurableActor / ActorConfig](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Configurable-Actors)** 🎭: \
  Typed actor/body pairs carry an ActorConfig asset into the runtime actor on possession. Create data-driven actor variants.

- **[PoolableActor / PoolableActorMonoBehaviour](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Poolable-Actors)** 🎭: \
  Actor subclasses create and release Unity bodies through Pool. Reuse spawned actors and their GameObjects.

- **[ActorSystemAssetLoadableConfig](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Actor-Asset-Loading)** 🎭: \
  A type-to-loader configuration resolves actor prefabs and supports asynchronous body loading/preload setup. Spawn actor bodies through interchangeable asset loaders.

## ⚙️ [Attribute System](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Gameplay-and-Attributes)

V1 and V2 APIs coexist in separate namespaces. Select the API expected by the consuming code; their definitions, entities and modifiers are not interchangeable.

### ⚙️ [Attribute System V1](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Gameplay-and-Attributes)

- **[Attribute / AttributeConfig (V1)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Attribute-System-V1)** ⚙️: \
  Original configurable attributes support types, conditions, modifier operators and propagation rules. Maintain existing stats and buff/debuff logic.

- **[Entity / EntityConfig / EntityManager (V1)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Attribute-Entities-V1)** 🧑‍🤝‍🧑: \
  Entity configs/data, a manager and factory assemble runtime entities with V1 attributes. Instantiate and organize entities in the original attribute API.

### ⚙️ [Attribute System V2](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Gameplay-and-Attributes)

- **[AttributeDefinition / AttributeInstance / AttributeValue (V2)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Attribute-Definitions-and-Instances-V2)** ⚙️: \
  Typed integer/float/flag definitions and runtime instances support visibility, base values, clamping and fixed/dynamic limits. Author reusable stats with explicit presentation and value rules.

- **[EntityDefinition / Entity / EntityScopeTree (V2)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Attribute-Entities-and-Scopes-V2)** 🧑‍🤝‍🧑: \
  Entity definitions/runtime entities own attributes and expose a scope tree plus expression variable providers. Resolve stats across related entities such as owner, parent or root.

- **[AttributeModifierCoordinator / AttributeModifierApplier / EntityModifierSession (V2)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Attribute-Modifiers-V2)** ⚙️: \
  Coordinator/applier/session APIs support direct or reactive modifiers with sorting, stacking and missing-target policies. Apply equipment bonuses, auras and live buffs between entities.

- **[ExpressionModifierConditionEvaluator (V2)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Expression-Based-Modifier-Conditions)** ⚙️: \
  A generic-expression evaluator and compiled-expression cache determine modifier eligibility with entity scopes. Define conditional buffs through expressions and optional code predicates.

## 🎵 [Bragi Audio System](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Audio)

- **[Bragi Audio Hub](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Bragi-Audio-Hub)** 📜: \
  Spawns pooled AudioHandlers and plays configs/groups at a position or parent with several group playback modes. Centralize sound-effect playback and reuse audio sources.

- **[AudioConfig / AudioSettings / Addressables Audio Loader](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Audio-Configs-and-Settings)** 🎵: \
  Asset-based clips/loaders and playback settings configure volume, pitch, looping, spatial behavior and fades. Reuse sound definitions across gameplay and UI.

- **[AudioGroup](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Audio-Groups)** 🎵: \
  Weighted config groups play randomly, sequentially, in a chain or simultaneously. Vary repeated effects and compose multi-part sounds.

- **[AudioHandler](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AudioHandler)** 🔊: \
  Owns an AudioSource with playback state, fade controls and play/finish/stop events; disposal returns it to the pool. Control individual playing sounds and observe completion.

- **[Jukebox / JukeboxConfig](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Jukebox-Playlists)** 🎶: \
  Sequential/random/reseeded playlists support looping, navigation, mute, pause and automatic continuation. Play background music and authored playlists.

- **[UIAudioTrigger](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UIAudioTrigger)** 🔊: \
  Maps pointer, selection, drag, lifecycle and custom-string events to audio configs with optional parallel-play prevention. Add interaction sounds to menus and controls.

## ⏳ [Chronos System (Time and Offline Progress)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Persistence-and-Time)

- **[Chronos](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Chronos-Time-and-Offline-Progress)** ⏳: \
  Synchronizes real-world UTC time, exposes time-trust status and tracks elapsed time across application close, pause and focus changes. Calculate offline rewards and time-based progression.

- **[DateTimeProvider / TimeApiIoProvider / WorldTimeApiProvider / TimeMachineProvider](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Chronos-Time-Providers)** ⏳: \
  Ordered fallback providers supply system time, TimeAPI.io, WorldTimeAPI or editor-controlled simulated time. Choose and test the time source used by Chronos.

- **[Chronos Security, Offline and Persistence Policies](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Chronos-Trusted-State-and-Policies)** 💾: \
  Security/offline policies, signed persisted state and synchronization cooldowns govern time trust and tamper status. Control whether offline elapsed time is accepted after clock changes or failed sync.

- **[ChronosTimeEditorWindow](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Chronos-Time-Control-Editor)** ⏳: \
  Editor bridge/window inspect Chronos and control simulated time through the time-machine provider. Test offline progression and time synchronization scenarios.

## 🎼 [Maestro System (Initialization and Task Management)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Async-and-Startup)

- **[GameInitialization / InitStepConfig / InitStepListingConfig](https://github.com/LeGustaVinho/legendary-tools-common/wiki/GameInitialization)** 🚀: \
  Builds a game startup sequence from configurable initialization steps and a step listing. Initialize assets, persistence and UI in a defined order.

- **[Maestro](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Maestro-Task-Orchestration)** 🎼: \
  Runs tasks with dependencies, timeout tracking and optional internet requirements. Order service setup and report startup failures.

- **[Maestro Initialization Graph Editor](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Maestro-Graph-Editor)** 📈: \
  Authors initialization-step relationships using the reusable node editor. Inspect and edit a startup dependency graph.

## 🖥️ [Screen Flow System (UI Navigation)](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-UI-and-Navigation)

- **[ScreenFlow / ScreenFlowConfig / ScreenConfig](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenFlow-Navigation)** 🖼️: \
  Config-driven screens and popup stacks use queued transitions, history, back behavior, scene entries and asset loading. Build menus, screen transitions and modal navigation.

- **[ScreenBase / ScreenViewController and Animated Variants](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Screen-Views-and-Controllers)** 🖥️: \
  Screen bases, generic/animated variants and view-controller contracts carry arguments and transition lifecycle behavior. Implement strongly typed screen presentation logic.

- **[PopupBase / PopupConfig / PopupScreenViewController and Animated Variants](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Popup-Views-and-Controllers)** 🖥️: \
  Popup bases, animated variants, typed arguments and background/transition policies integrate modal views with ScreenFlow. Implement confirmation dialogs and stacked modal windows.

- **[UIScreenFlowTrigger / Back-Key Rules](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenFlow-UI-Triggers-and-Back-Rules)** 🖥️: \
  Scene/UI triggers issue configured flow commands while screen rules determine back-navigation behavior. Wire menu buttons and back controls in the Inspector.

- **[ScreenFlow Canvas, Config and Inspector Tools](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenFlow-Editor-Setup)** 🔄: \
  Editor actions create flow canvases/config assets and inspect screen-flow definitions. Bootstrap the Unity hierarchy and assets needed for navigation.

## 🖼️ [UI Components](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-UI-and-Navigation)

- **[CircularScrollView](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CircularScrollView)** 🔄: \
  Repositions scrolling items in a loop and exposes selected-item changes. Build carousels and repeating selectors.

- **[DynamicScrollView](https://github.com/LeGustaVinho/legendary-tools-common/wiki/DynamicScrollView)** 📜: \
  Generic ScrollRect view creates/removes visible data items and supports refresh and scroll-to operations. Display large item lists with limited visible GameObjects.

- **[ScrollRectViewportUtility](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScrollRect-Viewport-Utilities)** 🖼️: \
  Shared helpers calculate expanded viewport bounds and visible uniform-item ranges. Implement buffered recycling and uniform-list virtualization.

- **[GameObjectListing](https://github.com/LeGustaVinho/legendary-tools-common/wiki/GameObjectListing)** 📋: \
  Generic data-to-prefab listing creates initialized views, filters data and removes generated items. Build dynamic menus and small repeated UI collections.

- **[FieldSync / FieldSyncEditor](https://github.com/LeGustaVinho/legendary-tools-common/wiki/FieldSync)** 🔄: \
  Reflection-based field/property synchronization links an origin and destination in edit and play modes, with a custom Inspector. Mirror a selected value between Unity objects.

- **[UIDrag / UIDrop / DraggingObject](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UI-Drag-and-Drop)** 🖼️: \
  UIDrag/UIDrop coordinate pointer drags, slot acceptance, snapping, ownership and invalid-drop behavior. Build inventory slots and draggable UI editors.

- **[ProximityUi / ProximityUiBehaviour](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Proximity-UI)** 📡: \
  Pairs proximity detection with UIFollowTransform views and UI-created/removed events. Show labels and interaction prompts for nearby objects.

- **[UIFollowTransform](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UIFollowTransform)** 👣: \
  Projects a target transform into a configured canvas with camera and offset controls. Attach health bars or labels to world objects.

- **[UIGradient](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UIGradient)** 🌈: \
  Applies a four-corner bilinear color gradient through a UI mesh effect. Style UI graphics without adding a custom shader.

- **[UILineConnector](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UILineConnector)** 🔗: \
  Creates dynamic Image-based lines between RectTransforms with thickness, order, optional lifetime and pooling. Draw UI graph edges, skill links and temporary connectors.

- **[UIPanel](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UIPanel)** 🖼️: \
  CanvasGroup wrapper controls alpha, interaction and raycasts, with optional selectable disabling. Control panel visibility and input participation.

- **[UISafeArea](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UISafeArea)** 📱: \
  Adjusts RectTransform anchors for device safe areas, screen changes and optional pixel offsets. Keep mobile UI clear of cutouts and system margins.

## 🧪 [Source Generation, Tests and Validation](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Category-Developer-Tooling)

- **[Deterministic Source Generators](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Source-Generators)** 🧪: \
  Python scripts generate collection policies, codecs, Native wrappers, graph bindings, state adapters and hash vectors. Regenerate repetitive deterministic APIs from their shared definitions. Also generates persistent-graph wrappers and graph-delta bindings.

- **[Fixed-Point Source Generators](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Point-Source-Generators)** 🧪: \
  Python scripts generate scalar/vector/math/network/random/batch APIs and reference/replay corpora. Keep generated FixedPoint APIs and validation references synchronized.

- **[Unity Tests, Player Validation, Standalone Harnesses and Benchmarks](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Tests-and-Validation-Harnesses)** 🧪: \
  Unity tests, Managed/Burst comparison suites, player corpora, standalone executables and snapshot benchmarks exercise library behavior. Validate changes, compare backend results and measure representative workloads.

Further details: [Deterministic validation report](Tools/Deterministic/VALIDATION.md) · [Fixed-point validation report](Core/DataStructure/Deterministic/FixedPoint/VALIDATION.md).

## 🧩 [Common Feature Combinations](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations)

Existing integrations are connected by the library code. Suggested workflows require application-specific adapters, event wiring or data conversion.

- **[Reliable game startup](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#reliable-game-startup)** 🧩: \
  **Existing integration.** [`GameInitialization`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/GameInitialization) + [`Maestro`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Maestro-Task-Orchestration) + [`InternetProviderChecker and Implementations`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Internet-Connection-Checks) + [`AsyncWait`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AsyncWait) + [`Maestro Initialization Graph Editor`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Maestro-Graph-Editor). Initialization assets feed Maestro tasks; dependency and connectivity checks order startup, while the graph editor helps author the sequence.

- **[Configurable pooled actors](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#configurable-pooled-actors)** 🧩: \
  **Existing integration.** [`Actor`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Actor-Lifecycle-System) + [`ConfigurableActor`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Configurable-Actors) + [`PoolableActor`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Poolable-Actors) + [`ActorSystemAssetLoadableConfig`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Actor-Asset-Loading) + [`AssetLoaderConfig`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Asset-Provider-and-Loaders) + [`Pool`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Object-Pooling). Actor variants carry config assets, resolve prefab loaders and create/release pooled bodies. Select the actor subclass that matches the desired lifecycle.

- **[Music and interface sounds](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#music-and-interface-sounds)** 🧩: \
  **Existing integration.** [`Bragi Audio Hub`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Bragi-Audio-Hub) + [`AudioConfig`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Audio-Configs-and-Settings) + [`AudioGroup`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Audio-Groups) + [`AudioHandler`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AudioHandler) + [`Jukebox`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Jukebox-Playlists) + [`UIAudioTrigger`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UIAudioTrigger). Bragi manages handlers; authored configs/groups feed playlists and UI triggers. Handler completion advances Jukebox playback.

- **[Conditional live buffs](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#conditional-live-buffs)** 🧩: \
  **Existing integration.** [`AttributeDefinition`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Attribute-Definitions-and-Instances-V2) + [`EntityDefinition`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Attribute-Entities-and-Scopes-V2) + [`AttributeModifierCoordinator`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Attribute-Modifiers-V2) + [`ExpressionModifierConditionEvaluator`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Expression-Based-Modifier-Conditions) + [`ExpressionEngine`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Generic-Expression-Engine) + [`Expression Variable Providers and Scope Relations`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Expression-Providers-and-Scopes). V2 modifier sessions use expression-based eligibility and entity scope/variable adapters to keep reactive modifiers current.

- **[Reusable prefab lists](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#reusable-prefab-lists)** 🧩: \
  **Existing integration.** [`ViewDataListBinder`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ViewDataListBinder) + [`ViewDataBindingProfile`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Profiles-and-Contexts) + [`ViewDataBinder`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ViewDataBinder) + [`ViewDataEventBinder`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ViewDataEventBinder) + [`ScrollRectViewportUtility`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScrollRect-Viewport-Utilities). List items receive named contexts and runtime model injection before activation; child value/event binders can reuse profile definitions.

- **[Observable settings forms](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#observable-settings-forms)** 🧩: \
  **Existing integration.** [`Observable and Typed Wrappers`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Observable-Values) + [`Reactive Component Bindings`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reactive-Component-Bindings) + [`Fluent Reactive Binding Builders`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fluent-Reactive-Bindings) + [`BindingHandle`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reactive-Binding-Lifetime) + [`BindingsDebuggerWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reactive-Bindings-Debugger). Component binding adapters synchronize Observable values and UI; handles/anchors manage cleanup and the debugger displays registered bindings.

- **[Nearby world labels](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#nearby-world-labels)** 🧩: \
  **Existing integration.** [`ProximityDetector`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ProximityDetector) + [`ProximityUi`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Proximity-UI) + [`UIFollowTransform`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UIFollowTransform). ProximityUi creates/removes follow-transform UI views as detectable objects enter or leave the trigger.

- **[Screen and modal navigation](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#screen-and-modal-navigation)** 🧩: \
  **Existing integration.** [`ScreenFlow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenFlow-Navigation) + [`ScreenBase`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Screen-Views-and-Controllers) + [`PopupBase`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Popup-Views-and-Controllers) + [`UIScreenFlowTrigger`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenFlow-UI-Triggers-and-Back-Rules) + [`ScreenFlow Canvas, Config and Inspector Tools`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenFlow-Editor-Setup) + [`AssetLoaderConfig`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Asset-Provider-and-Loaders). Flow configs, loaders, queued commands and popup policies coordinate authored view lifecycles and navigation history.

- **[Trusted offline progression](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#trusted-offline-progression)** 🧩: \
  **Existing integration.** [`Chronos`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Chronos-Time-and-Offline-Progress) + [`DateTimeProvider`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Chronos-Time-Providers) + [`Chronos Security, Offline and Persistence Policies`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Chronos-Trusted-State-and-Policies) + [`Persistence`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Persistence) + [`ChronosTimeEditorWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Chronos-Time-Control-Editor). Chronos provider sync and signed persisted state establish time status; the time-control window helps test elapsed-time scenarios.

- **[Deterministic rollback world](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#deterministic-rollback-world)** 🧩: \
  **Existing integration.** [`Fixed32`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Point-Scalars) + [`FixedRandom`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/FixedRandom) + [`Fixed-Point Policies`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Point-Deterministic-Adapters) + [`Deterministic Arrays, Cells, Lists, Queues, Deques, Stacks and Ring Buffers`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Sequence-Collections) + [`DeterministicStateRegistry`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-State-Registry) + [`DeterministicInputHistory`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Input-History) + [`DeterministicWorldHistory`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-World-History) + [`DeterministicSimulationRunner`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Lockstep-and-Rollback-Simulation) + [`DeterministicEventLedger`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Event-Ledger). Register deterministic state and RNG, freeze compatibility rules, then drive tick inputs and checkpoint retention. Keep presentation reactions behind event confirmation/retraction handling.

- **[Replayable network matches](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#replayable-network-matches)** 🧩: \
  **Existing integration.** [`DeterministicSimulationRunner`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Lockstep-and-Rollback-Simulation) + [`DeterministicNetworkSession`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Network-Sessions) + [`DeterministicRaftNode`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Raft-Coordination) + [`IDeterministicTransport`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Transport-and-Loopback-Network) + [`DeterministicReplayArchive`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Replay-Archive) + [`Canonical Checkpoints, Field Codecs and Hashing`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Canonical-Checkpoints-and-Codecs). Sessions coordinate inputs/commits and migration; canonical checkpoints and replay archives restore or inspect a match. Supply the real application transport.

- **[Focused AI coding context](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#focused-ai-coding-context)** 🧩: \
  **Existing integration.** [`RoslynNuGetInstaller`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Roslyn-Installer) + [`TypeIndex`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CSharp-Type-Index) + [`CodeDependencyScanner`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Source-Dependency-Scanner) + [`CSFilesAggregatorWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CSharp-Files-Aggregator) + [`CSharpImplementationStripper`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/CSharp-Implementation-Stripper) + [`AICodePasteHubWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AI-Clipboard-Pipeline) + [`AiFriendlyCompilationErrorReporter`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Compilation-Error-Reports) + [`ManualCodeControlWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Manual-Code-Control). Type-index/dependency tooling expands aggregation inputs; transforms reduce output. Clipboard application and compile reports support the follow-up edit/compile cycle.

- **[Inventory screen](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#inventory-screen)** 🧩: \
  **Suggested workflow.** [`Inventory`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Inventory) + [`ViewDataListBinder`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ViewDataListBinder) + [`ViewDataBinder`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ViewDataBinder) + [`UIDrag`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UI-Drag-and-Drop) + [`ScreenFlow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenFlow-Navigation) + [`UIAudioTrigger`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UIAudioTrigger). Project inventory quantities into item rows, bind labels and wire drag/drop commands back to inventory operations. Add application-specific item/slot rules and refresh triggers.

- **[Hex-grid pathfinding](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#hex-grid-pathfinding)** 🧩: \
  **Suggested workflow.** [`Hex`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Hex-Grid) + [`AStar`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AStar-Pathfinding) + [`PriorityQueue`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Priority-Queues) + [`MeshUtil`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/MeshUtil). Use hex coordinates/layout for the board and implement IAStar neighbors/heuristic over its cells; generate board visuals with mesh helpers.

- **[Data-driven content pipeline](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#data-driven-content-pipeline)** 🧩: \
  **Suggested workflow.** [`SpreadsheetImporterWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Spreadsheet-Importer) + [`MultiScriptableObjectEditor`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Multi-Asset-ScriptableObject-Editor) + [`ScriptableObjectVariant`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScriptableObject-Variants) + [`DictionaryConfigEnumWeaver`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Config-to-Enum-Weaver) + [`DictionaryConfigNamesWeaver`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Config-Name-Constants-Weaver). Import tabular configs, compare them in the multi-asset editor, author variants where appropriate and regenerate identifiers for supported config listings.

- **[Large-world interaction UI](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#large-world-interaction-ui)** 🧩: \
  **Suggested workflow.** [`GlobalPosition`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Global-Coordinates) + [`FloatingOriginService`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Floating-Origin) + [`SpatialHashGridSystem`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Spatial-Hash-Scene-Integration) + [`ScreenToWorldInfo`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/ScreenToWorldInfo) + [`UIFollowTransform`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UIFollowTransform). Use global coordinates for persistent locations and local transforms for presentation; explicitly refresh spatial bounds after origin shifts and connect pointer/UI behavior.

- **[Asset cleanup investigation](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#asset-cleanup-investigation)** 🧩: \
  **Suggested workflow.** [`AssetGuidMapper`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/AssetGuidMapper) + [`ReferenceTrackerWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reference-Tracker) + [`MissingReferenceScannerWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Missing-Reference-Scanner) + [`OrphanAssetScannerWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Orphan-Asset-Scanner) + [`AssetNavigatorWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Asset-Navigator). Trace references, repair broken links and review orphan candidates with knowledge of dynamic loading before deciding what to remove.

- **[Performance investigation](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#performance-investigation)** 🧩: \
  **Suggested workflow.** [`DomainReloadAnalyzerWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Domain-Reload-Analyzer) + [`ManualCodeControlWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Manual-Code-Control) + [`RenderingPerformanceHubWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Rendering-Performance-Hub) + [`RendererAuditWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Renderer-Audit) + [`UITextureSizeOptimizerWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/UI-Texture-Size-Optimizer) + [`Binding Inspectors, Member Pickers and Runtime Statistics`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Binding-Inspector-and-Diagnostics). Choose the analyzer for the observed bottleneck: code reload, rendering, UI texture imports or binding execution. Use findings to target changes.

- **[Recoverable component setup](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#recoverable-component-setup)** 🧩: \
  **Suggested workflow.** [`SerializedSnapshotLibrary`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Serialized-Snapshot-Library) + [`CopySerializedValuesWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Copy-Serialized-Values) + [`ReferenceTransferToolWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reference-Transfer) + [`ReferenceTrackerWindow`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Reference-Tracker). Capture a reusable serialized setup, preview application and remap references to the target hierarchy; inspect reference results afterward.

- **[Bounded deterministic ECS-style data](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Common-Combinations#bounded-deterministic-ecs-style-data)** 🧩: \
  **Suggested workflow.** [`DeterministicSparseSet`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-SparseSet) + [`DeterministicSlotMap`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-SlotMap) + [`DeterministicCommandBuffer`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-Command-Buffers) + [`DeterministicStateRegistry`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Deterministic-State-Registry) + [`FixedBatch`](https://github.com/LeGustaVinho/legendary-tools-common/wiki/Fixed-Point-Unity-Integration). Use application-owned entity IDs, dense components and ordered command merging, then register all logical state. Complete Native jobs before publishing checkpoints.
