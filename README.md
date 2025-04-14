# Legendary Tools

Legendary Tools are small tools that were created during my time as a Game Developer

**Legendary Tools - Commons** is the code shared between all tools

Legendary Tools Repos:

- [Bragi Audio:](https://github.com/LeGustaVinho/bragi-audio "Bragi Audio:") Makes managing and playing audio in Unity easy and streamlined
- [Screen Flow](https://github.com/LeGustaVinho/screen-flow "Screen Flow"): Makes managing and switching UI/UX screens easy
- [Maestro](https://github.com/LeGustaVinho/maestro "Maestro"): Create and execute dependency tree or initialization tree in a practical and clear way
- [State Machine](https://github.com/LeGustaVinho/state-machine "State Machine"): Create Finite State and Hierarchical Machines easily via code
- [Hex Grid](https://github.com/LeGustaVinho/hex-grid "Hex Grid"): Creates hegonal grids in an easy and optimized way
- [ServiceLocator](https://github.com/LeGustaVinho/service-locator "ServiceLocator"): Make services and system globally available without using Singletons
- [Find In Files](https://github.com/LeGustaVinho/find-in-files "Find In Files"): Discover and list references among Unity files
- [Graphs](https://github.com/LeGustaVinho/graphs "Graphs"): Create any type of graph (or tree) with this data structure
- [Dynamic Scroll View](https://github.com/LeGustaVinho/dynamic-scroll-view "Dynamic Scroll View"): Create recyclable Scrollview, allowing hundreds of items to scroll without loss of performance
- [Circular Scroll View](https://github.com/LeGustaVinho/circular-scroll-view "Circular Scroll View"): Make a circular scrollview (AKA carousel) easily and quickly
- [ScriptableObject Factory](https://github.com/LeGustaVinho/scriptable-object-factory "ScriptableObject Factory"): Create any ScriptableObject from a visual menu in the editor
- [Actor System](https://github.com/LeGustaVinho/actor "Actor System"): Decouble game logic from MonoBehaviour

🧩 Runtime
----------

### 📦 Data Structures

*   **Observable / ObservableList**: Reactive values and collections with events for change tracking.
    
*   **Bictionary**: Bidirectional dictionary with fast key/value lookup in both directions.
    
*   **CircularBuffer / MovingAverage**: For rolling average computations and circular queue behaviors.
    
*   **SerializedDateTime / SerializedTimeSpan**: Serializable time structures for Unity's serialization system.
    
*   **OneToManyMap / ManyToManyMap**: Map structures for parent-child and bidirectional many-to-many relationships.
    

### 🎒 Inventory System

*   **Inventory / IInventory**: Inventory that tracks quantities and fires change events.
    
*   **ScriptableObjectInventory**: ScriptableObject-based inventory using enum-config mapping.
    
*   **CargoContainer**: Container with a limit for transport-style inventory logic.
    

### 🔍 Detection & Sensors

*   **ProximityDetector**: Detects when objects enter or exit a collider.
    
*   **VisibilityDetector**: Detects renderer visibility from the camera.
    

### 🏷️ Tag System

*   **Tag / ITaggable / TagFilter**: Add and query custom tags with filtering logic (Include/Exclude rules).
    

### 🌍 Spatial Partitioning

*   **Octree / Quadtree**: Efficient spatial data structures for 3D and 2D queries.
    
*   **BoundingBox / Rectangle**: Used for spatial containment and intersection logic.
    

### 🌐 Networking

*   **PingInternetProviderChecker**: Checks internet connectivity by pinging IP.
    
*   **UnityInternetProviderChecker**: Uses Application.internetReachability.
    

### 🧠 Command Pattern

*   **ICommand / IAsyncCommand**: Interfaces for synchronous and asynchronous command pattern usage.
    

### 🧬 Unique Identity System

*   **IUnique / UniqueBehaviour / UniqueScriptableObject**: Assign and persist GUIDs to objects.
    
*   **UniqueBehaviourReference**: Reference system that resolves instances from GUIDs at runtime.
    
*   **UniqueObjectListing**: Central registry of all uniquely identified objects.
    

### 🧠 Threads & Async

*   **AsyncRoutine**: Coroutine-like class supporting thread-switching (background ↔ main thread).
    

### 🔢 Math & Color Utilities

*   **MathUtil / CurveUtil**: Random generation, interpolation, and spline functions.
    
*   **HSV**: Struct for working with HSV colors and converting to/from Color.
    

### 🔀 Extensions (Runtime)

*   **EnumerableExtension / StringExtension / EnumExtension / TypeExtension**: Utility functions for iteration, type resolution, parsing, enum handling, etc.
    
*   **UnityExtension**: Adds helper methods for working with Unity UI and GameObjects.
    

🛠️ Editor
----------

### 🎛️ Editor Windows & Tools

*   **PlayerPrefsEditor / PlayerPrefsTools**: Visualize and manage PlayerPrefs directly from the editor.
    
*   **DefineSymbolsEditor**: Manage scripting define symbols per build target.
    
*   **CommandGenerator**: Generates command classes from selected types.
    
*   **AssetHistoryWindow**: Track and navigate selection history in Unity Editor.
    
*   **AssetUsageFinder**: Find references to selected assets across project files.
    
*   **CopySerializedValuesWindow**: Copy matching fields from one component to another.
    
*   **AggregateCodeFiles / CSFilesAggregator**: Combine multiple .cs files into single files or text blobs.
    

### 📊 Spreadsheet Tools

*   **SpreadsheetImporterWindow**: Imports CSV/Google Sheets to:
    
    *   Create individual ScriptableObjects.
        
    *   Populate ScriptableObject collections.
        
    *   Supports OAuth2 + field mapping + presets via configuration assets.
        

### 🧪 Serialization

*   **Serialization**:
    
    *   Supports XML, Binary, Odin serialization (binary & JSON).
        
    *   Methods for file and memory-based persistence.
        

### 🔨 Property Drawers

*   **SerializedDateTimeDrawer / SerializedTimeSpanDrawer**: Custom inspector UIs for serializable structs.
    
*   **SerializableTypeDrawer**: Type selection from a list of concrete types.
    

### 🧵 Config Weaving

*   **DictionaryConfigEnumWeaver**: Generates enums and maintains mappings to ScriptableObjects.
    
*   **ConfigListing**: Auto-discovers and manages lists of configs (ScriptableObjects).
    

### 🧮 Utilities

*   **WeaverUtils**: Generates .cs classes or enums programmatically using templates.
    
*   **UnityFilePath**: Path struct that builds Unity-safe file paths (streaming assets, persistent, etc.).
    
*   **PrintSaver**: Captures screenshots with timestamped filenames when pressing a key.