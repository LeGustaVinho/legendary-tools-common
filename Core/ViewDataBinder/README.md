# ViewDataBinder

A declarative Unity data binding framework configured entirely from the Inspector.

## Supported instance roots

- `UnityObject`: any `UnityEngine.Object`, including `MonoBehaviour`, `ScriptableObject`, components and assets.
- `StaticType`: any type exposing public static fields or properties. This also supports singleton paths such as `GameState.Instance.Player.Health`.
- `Provider`: any `UnityEngine.Object` implementing `IBindingInstanceProvider`. The provider can expose any plain C# object at runtime.
- `Context`: a named root resolved from a binding profile override or the nearest active `BindingDataContext` in the hierarchy. Profile definitions conventionally use `$Source` and `$Target`.

## Binding directions

- `SourceToTarget`
- `TargetToSource`
- `TwoWay`

Two-way bindings initialize from Source to Target before reverse conversion is attempted. This allows targets such as an initially empty text input to be initialized by a reversible numeric converter. After initialization, the binder tracks which side changed since the previous synchronization pass. If both sides change before the same pass, `BindingConflictResolution` decides the winner. Values are read back after two-way writes so setters that clamp or normalize values establish the correct runtime baseline.

## Polling timings

- Awake
- Start
- Update
- LateUpdate
- FixedUpdate
- Manual

Use `SynchronizeManualBindings()` for bindings configured as Manual, or `SynchronizeAll()` to force all configured bindings.

Selective APIs avoid scanning unrelated bindings:

```csharp
binder.SynchronizeManualBinding(bindingIndex);
binder.SynchronizeManualBinding("PlayerHealth"); // ID or exact name
binder.SynchronizeBinding(bindingIndex);          // Force one binding
binder.SynchronizeBinding("PlayerHealth");       // ID or exact name
binder.SynchronizeManualBindingsForSource(player);
binder.InvalidateBinding("PlayerHealth");        // Clears baseline and error-disable state
```

`ViewDataEventBinder` provides matching `ProcessManualBinding`, `ProcessEventBinding`, `ProcessManualBindingsForSource` and `InvalidateEventBinding` APIs.

Unidirectional bindings expose a `Write Policy`:

- `When Value Changes` avoids redundant setter calls when the converted output is equal to the previous successful output.
- `Always` writes on every polling pass when the binding must remain authoritative over external changes.

Disabling a binder component clears its runtime baselines. Re-enabling it performs clean initialization instead of treating changes made while disabled as conflicts or event transitions.


## Reusable binding profiles

`ViewDataBindingProfile` is a `ScriptableObject` containing a reusable group of `ViewDataBinding` definitions. Create assets such as `PlayerHudBindingProfile`, `InventoryItemBindingProfile` or `SettingsMenuBindingProfile`, then add the same asset to multiple `ViewDataBinder` components.

Inside a profile, configure Source endpoints with `Instance Kind = Context` and context `$Source`. Configure Targets with `$Target`. Every profile reference on a binder supplies its own concrete Source and Target roots, so the same definition can be applied to different prefab instances.

The binder Inspector includes `Create from Local`. It:

1. verifies that the local bindings share one Source root and one Target root;
2. copies the bindings into a new profile asset;
3. replaces concrete roots in the asset with `$Source` and `$Target` contexts;
4. adds a disabled profile reference containing the original roots.

The new reference starts disabled so the profile does not run alongside the original local bindings. Remove or disable the local copies, then enable the profile reference.

Additional named roots can be supplied through `Named Contexts`. A child binder can also inherit named values from the nearest active `BindingDataContext`. This is useful for shared services such as `Localization`, `Settings`, `Inventory` or `Session`.

Profile roots accept Unity objects, instance Providers and static types. They can also be replaced at runtime without modifying the asset:

```csharp
binder.SetProfileSourceRoot(profileIndex, playerViewModel);
binder.SetProfileTargetRoot(profileIndex, hudGameObject);
binder.SetProfileContext(profileIndex, "Localization", localizationService);

// Remove the runtime override and return to the serialized profile value.
binder.ClearProfileContext(profileIndex, "Localization");
```

Changing a runtime profile context invalidates only that profile reference's runtime baselines and endpoint caches.

## Explicit endpoint lifetime

Each value binding has independent `Missing Source` and `Missing Target` policies. Event bindings expose `Missing Source`.

- `Wait`: keep the binding active, suppress the missing-endpoint failure and retry on the next scheduled pass.
- `Disable`: disable that binding until `InvalidateBinding`, `InvalidateProfileBinding` or the Inspector `Reset` button is used.
- `ClearTarget`: when a Source is missing, write the default value of the Target type once, then wait. A missing Target cannot be cleared.
- `UseFallback`: write the configured fallback once to the surviving side when its direction permits it, then wait.
- `ReResolve`: invalidate only the binding's endpoint/context caches, reset its baseline and retry immediately.
- `ReportError`: return the unresolved-instance failure and let the binding's `Error Policy` decide whether to report, log, disable or throw.

Missing-instance detection includes destroyed Unity objects, null Provider results, unresolved profile/context roots and removed component roots. Invalid member names remain configuration errors and are not treated as temporary lifetime events.

When an endpoint becomes available again, the binder resets that binding's comparison baseline before synchronization. This ensures a newly pooled or additively loaded Target receives its initial value even if it replaces an object that previously held the same value. `ClearTarget` and `UseFallback` actions are remembered so they do not repeat writes every frame while the endpoint remains unavailable.

## Error policies

Every value binding and event binding has an independent `Error Policy`:

- `ReportOnly`: retain the failure in `LastResult` without writing to the Console.
- `LogOnce`: log one warning per error episode. A successful pass resets the episode.
- `LogEveryTime`: log every failed pass.
- `DisableUntilReset`: stop processing the binding after its first failure. Call `InvalidateBinding` or `InvalidateEventBinding`, or use the per-binding `Reset` button in Play Mode, to retry.
- `ThrowException`: throw an `InvalidOperationException` after storing the failed result.

The policy runs only on failures. `Success`, `NoChange` and explicitly disabled bindings are not treated as errors.

## Type safety

Without a Converter, the runtime only synchronizes endpoints whose effective Source output type and Target type are exactly equal. No implicit conversion is performed.

Assign a `BindingConverter` ScriptableObject to explicitly bridge different types. Converter compatibility is validated in the Inspector and again at runtime.

## Multiple Sources

`ViewDataBinding` stores a `List<BindingSource>`. There are two supported multi-source paths:

- Enable a Formatter. The formatter reads every configured Source and produces one typed output for the Target.
- Replace `BindingBackendRegistry.SourceBackend` with a custom composition backend for non-formatter scenarios.

The default `SingleSourceBindingSourceBackend` still requires exactly one Source when no Formatter is enabled.

```csharp
BindingBackendRegistry.SourceBackend = new MyCompositeSourceBackend();
```

## Replacing Reflection

The default member backend is `ReflectionBindingMemberBackend` and caches member trees, resolved member paths and complete endpoint resolutions. Component-backed endpoints therefore avoid repeated component scans during polling. Direct one-member writes use an allocation-free fast path, and formatted bindings reuse their runtime value and metadata buffers.

Replace it with a generated, expression-tree, IL or source-generated implementation:

```csharp
BindingBackendRegistry.MemberBackend = new GeneratedBindingMemberBackend();
```

Instance resolution is independently replaceable:

```csharp
BindingBackendRegistry.InstanceResolver = new MyBindingInstanceResolver();
```

## Provider example

```csharp
using System;
using LegendaryTools.ViewBinding;

public sealed class PlayerStateProvider : BindingInstanceProviderBehaviour
{
    private PlayerState playerState;

    public override object GetBindingInstance()
    {
        return playerState;
    }

    public override Type GetBindingInstanceType()
    {
        return typeof(PlayerState);
    }
}
```

## Inspector extensions

Implement `IViewDataBindingInspectorExtension` in an Editor assembly and register it:

```csharp
BindingInspectorExtensionRegistry.Register(new MyBindingInspectorExtension());
```

Extensions choose a `BindingInspectorExtensionPlacement` slot (`BeforeSources`, `AfterSources`, `BeforeTarget`, `AfterTarget` or `AfterValidation`) and an `Order`. They receive `BindingInspectorContext`, including the binder, serialized object, current binding property and binding index.

## IL2CPP and managed stripping

Member paths are string-based and accessed through reflection. Types or members that have no static references may require preservation in builds that use managed code stripping. Add `[UnityEngine.Scripting.Preserve]` to reflected model types or include the relevant assemblies and types in the project's `link.xml`. A generated `IBindingMemberBackend` can avoid this requirement for performance-critical or aggressively stripped builds.

## Folder layout

Copy the entire folder under `Assets`, preserving the `Editor` subfolder so editor-only code is excluded from player builds.

## GameObject member browsing

When a `UnityObject` root is a `GameObject`, the member picker shows a `GameObject` group plus one group for every attached component. Multiple components of the same type are numbered separately. Component-backed paths are resolved by component type and same-type ordinal at runtime, so the binding reads and writes the selected component rather than the `GameObject` itself.

## Member search performance

The Reflection backend implements `IBindingMemberSearchBackend`, so member searches are executed directly against cached type metadata without materializing or recursively rescanning the full visual tree. Results are cached per query and displayed as a flat result list while the normal unfiltered browser remains a tree. Results are capped at 250 visible rows so broad searches do not overload IMGUI rendering. The backend also bounds its query cache, preventing arbitrary search text from growing the cache for the lifetime of the Editor session.

Custom member backends can implement `IBindingMemberSearchBackend` for the same fast search path. Backends that do not implement it automatically fall back to a reusable flat index built by the picker.

The Reflection backend also caches the public bindable `MemberInfo` list for each type/static-mode pair, avoiding repeated `GetFields` and `GetProperties` work while trees and nested paths are explored.

## Target Inspector quality-of-life

- Target picker rows whose type matches the expected Target type are highlighted in green. Without a Converter this is the effective Source/Formatter output type; with a Converter it is the converter Target type.
- The Target section evaluates the Source pipeline and shows a `Value Preview` through `ToString()`, including formatter, converter, null handling and fallback behavior.
- Component-backed serialized paths are displayed as readable paths such as `Transform.position.x` instead of their internal component path representation.

## Formatters

A binding can enable a formatter between its Sources and Target. Formatter bindings support multiple Sources and currently run in the `SourceToTarget` direction only because formatted output is not reversible.

The built-in `CompositeStringBindingFormatter` uses standard composite formatting:

```text
Sources:
  Source 1 -> Player.Name
  Source 2 -> Player.Health
  Source 3 -> Player.MaxHealth

Format String:
  {0} - HP {1}/{2}

Target:
  Label.text
```

`Culture Name` is optional. Leave it empty to use `CultureInfo.CurrentCulture`, or provide a culture such as `en-US` or `pt-BR`.

Formatters are extensible. Implement `IBindingFormatter` and register it:

```csharp
BindingFormatterRegistry.Register(new MyBindingFormatter());
```

A formatter declares its output `Type`, which is used for Target validation and compatible-member highlighting in the Target picker.


## Converters

Converters are reusable `ScriptableObject` assets referenced by a binding. They run after the Source pipeline and optional Formatter:

```text
Sources -> Formatter (optional) -> Converter (optional) -> Target
```

For reverse directions, the pipeline is:

```text
Target -> Converter.ConvertBack -> Source
```

`TargetToSource` and `TwoWay` require a converter that supports reverse conversion. The Target member picker uses the converter Target type for green compatibility highlighting, and `Value Preview` shows the final converted value.

Create custom converters by deriving from the generic base:

```csharp
using LegendaryTools.ViewBinding;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Binding Converters/Health To Text")]
public sealed class HealthToTextConverter : BindingConverter<float, string>
{
    public override bool SupportsReverseConversion => false;

    protected override bool TryConvertValue(
        float sourceValue,
        out string targetValue,
        out string error)
    {
        targetValue = $"HP: {sourceValue:0}";
        error = string.Empty;
        return true;
    }
}
```

The package includes ready-to-use converter assets:

- `ToStringBindingConverter`: converts any non-null Source value to `string` through `ToString()`; forward only.
- `FloatStringBindingConverter`: converts `float <-> string`, supports a numeric format and optional culture name, and is reversible.
- `NumericBindingConverter`: converts between configured primitive numeric types and supports reverse conversion.
- `BooleanNegationBindingConverter`: converts `bool <-> bool` by negating the value.
- `ObjectNullBindingConverter`: converts any Source type to `bool` based on CLR and Unity fake-null semantics.
- `StringBooleanBindingConverter`: maps configurable true/false strings to `bool` and back.
- `EnumToStringBindingConverter`: formats any enum as a string; forward only.

The converter asset Inspector displays its forward and reverse type contract. The same asset can be reused by any number of bindings.

### Converter fallback

`Fallback` values remain in the effective Source output domain, before conversion. This makes one fallback value coherent for forward and reverse bindings.

Example:

```text
Source float: 10.5
Fallback float: 0
Converter: FloatStringBindingConverter
Target string: "10.5"
```

When `On Converter Failure` is enabled, a failed forward conversion retries using the fallback Source value. A failed reverse conversion writes the fallback Source value directly.

## Fallback values

Each binding can enable a typed fallback value. The Inspector chooses the fallback editor from the effective Source/Formatter output type, before conversion.

Fallbacks can be used for:

- null values through `Null Handling = UseFallback`;
- Source or Target read failures through `On Read Failure`;
- formatter failures through `On Formatter Failure`;
- converter failures through `On Converter Failure`.

Common CLR and Unity types receive typed Inspector controls. Enums use a popup, `UnityEngine.Object` values use a constrained object field, and unsupported custom serializable types use JSON through `JsonUtility`.

## Null handling

Each binding has an explicit null policy:

- `PassThrough`: preserve null and let the formatter or Target receive it.
- `UseFallback`: replace null with the configured fallback value. When a Converter is assigned, the fallback is expressed in the pre-conversion Source/Formatter output type.
- `SkipSynchronization`: do not write anything during that synchronization pass.
- `SetDefaultValue`: use `default(T)` for the null value's declared type.
- `Fail`: stop synchronization and return `NullValueRejected`.

For formatter bindings, null handling is evaluated for every Source before formatting and once again for the formatter output. With `UseFallback`, any null Source selects the configured fallback instead of formatting a partial value. When a Converter is assigned, that fallback then passes through the Converter before being written to the Target.

Nested member paths now treat an intermediate null as a null read result. For example, if `Player.Profile` is null while reading `Player.Profile.Name`, the binding's null policy decides what happens instead of the Reflection backend failing before the policy can run.

Destroyed `UnityEngine.Object` references are also treated as null by the null policy.

## Value preview and indicators

Each binding card has a status dot:

- green: valid configuration or successful runtime synchronization;
- blue: no change, or an active Task on an event binding;
- amber: incomplete or invalid Editor configuration;
- red: runtime failure;
- gray: disabled.

The `Binding Preview` foldout is read-only and refreshed on demand. It shows:

- raw Source or formatter output;
- forward converter output;
- current Target value when readable;
- reverse converter output for `TargetToSource` and `TwoWay` bindings;
- the precise preview failure or null-policy message.

The public `TryGetPreview(index, out BindingPreview)` and `TryGetPreview(idOrName, out BindingPreview)` APIs expose the same data without writing either endpoint.

# ViewDataEventBinder

`ViewDataEventBinder` observes the same reusable `BindingSource` and `BindingEndpoint` model used by `ViewDataBinder`. It also shares the instance resolver, Reflection member backend, GameObject component grouping, fast member search, static type support and Provider support.

Both binder components derive from `BindingPollingBehaviour`, which centralizes the Unity polling lifecycle for:

- Awake
- Start
- Update
- LateUpdate
- FixedUpdate
- Manual

The Editor member endpoint UI is also shared through `BindingEndpointInspectorUtility`, while typed serialized constants are shared through `BindingSerializedValueDrawer`.

## Event binding structure

```text
ViewDataEventBinder
└── Event Binding
    ├── Polling
    ├── Sources
    │   ├── Source 1
    │   ├── Source 2
    │   └── ...
    └── Conditions
        ├── Condition
        │   ├── Clauses
        │   └── Actions
        └── ...
```

A Source member is observed by polling. The first successful read initializes the runtime state. By default no Action is invoked until an observed value changes. `Trigger On Initialize` can be enabled to evaluate Conditions immediately after the first successful observation.

## Conditions and clauses

Each Condition contains one or more Clauses. A Clause selects one of the Event Binding Sources and a comparison operator:

```text
==
!=
>
>=
<
<=
is null
is not null
is true
is false
```

Comparison constants are edited with the same typed serialized value UI used by binding fallback values.

Clauses after the first can be linked using:

```text
AND
OR
XOR
```

Each Clause can also enable `NOT`.

Logical precedence is:

```text
NOT
AND
XOR
OR
```

Example:

```text
Source 1: Player.Health
Source 2: Player.IsInvulnerable
Source 3: Player.IsDead

Condition:
Health <= 0
AND NOT IsInvulnerable is true
OR IsDead is true
```

A Condition is evaluated when any Source referenced by one of its Clauses changes. If the full expression is true, all Actions in that Condition are invoked.

## Actions

A Condition can contain zero or more Actions. An Action can invoke either a serialized UnityEvent or a public method returning exactly `System.Threading.Tasks.Task`. Both action types support four parameter modes:

```text
None
OldValue
NewValue
OldAndNewValues
```

`OldValue` and `NewValue` refer to the observed Source member whose change triggered the Condition evaluation. A Condition is invoked at most once per polling pass. If several Sources used by the same Condition change in that pass, the first changed Source found in Clause order supplies `OldValue` and `NewValue`.

The parameterized UnityEvents use:

```csharp
UnityEvent<object>
UnityEvent<object, object>
```

Task methods are selected from a Unity object, static type or Provider. Supported signatures are:

```csharp
public Task ExecuteAsync();
public Task ExecuteAsync(TValue oldValue);
public Task ExecuteAsync(TValue newValue);
public Task ExecuteAsync(TValue oldValue, TValue newValue);
```

The selected parameter mode determines whether zero, one or two arguments are required. Runtime values must be assignable to the declared parameter types. Generic methods, `Task<T>`, private methods, `ref` and `out` parameters are excluded.

`Prevent Concurrent` skips a new invocation while the previous Task is running. When disabled, concurrent Tasks are tracked and all exceptions are observed. The Event Binder invokes methods on Unity's main thread, never blocks while waiting, and monitors only bindings with active Tasks. It reports asynchronous failures through `BindingSyncResult` and applies the binding's Error Policy on the main thread. Disabling or invalidating a binding releases its tracking state but does not cancel the underlying Task; cancellation remains the responsibility of the invoked method.

`ProcessEventBindingDetailed(index, out triggered)` returns a `BindingSyncResult` that distinguishes no change, invalid endpoints, read failures, condition evaluation failures and action exceptions. `TryGetLastResult(index, out result)` exposes the most recent polling result while the original boolean `ProcessEventBinding(index)` API remains available.
