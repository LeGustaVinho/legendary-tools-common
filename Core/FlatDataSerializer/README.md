# FlatDataSerializer

Reflection-based flat serializer for Unity/C#.

## Supported

- Public fields and properties
- Nested classes and structs
- Primitive types, strings, enums, nullable values
- Arrays
- `List<T>` and `IList<T>`
- Null values
- Circular-reference detection
- Typed values
- Collection-to-table conversion
- Reconstruction from flat data

## Basic usage

```csharp
FlatObject flat = FlatDataSerializer.Serialize(player);
Player restored = FlatDataSerializer.Deserialize<Player>(flat);

FlatTable table = FlatDataSerializer.SerializeCollection(players);
List<Player> restoredPlayers =
    FlatDataSerializer.DeserializeCollection<Player>(table);
```

## Notes

- Types reconstructed through reflection need a public parameterless constructor,
  unless a custom `IObjectFactory` is supplied.
- Shared references are treated like circular references.
- Dictionaries, multidimensional arrays, polymorphic type metadata, and custom
  Unity value converters are not included in this first version.
- For IL2CPP builds, preserve reflected types with `[Preserve]` or `link.xml`
  when necessary.
