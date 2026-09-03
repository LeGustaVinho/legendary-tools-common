using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FlatData
{
    public static class FlatDataSerializer
    {
        public static FlatObject Serialize(
            object instance,
            FlattenOptions options = null)
        {
            FlattenOptions effectiveOptions = options ?? new FlattenOptions();
            return new ObjectFlattener(effectiveOptions).Flatten(instance);
        }

        public static T Deserialize<T>(
            FlatObject data,
            UnflattenOptions options = null)
        {
            return (T)Deserialize(typeof(T), data, options);
        }

        public static object Deserialize(
            Type targetType,
            FlatObject data,
            UnflattenOptions options = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            UnflattenOptions effectiveOptions =
                options ?? new UnflattenOptions();

            return new ObjectUnflattener(effectiveOptions)
                .Unflatten(targetType, data.Values);
        }

        public static FlatTable SerializeCollection(
            IEnumerable collection,
            FlattenOptions options = null)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            FlattenOptions effectiveOptions = options ?? new FlattenOptions();
            List<FlatRow> rows = new List<FlatRow>();
            SortedSet<string> columns =
                new SortedSet<string>(StringComparer.Ordinal);

            Type itemType = null;
            int index = 0;

            foreach (object item in collection)
            {
                if (item == null)
                {
                    throw new InvalidOperationException(
                        $"Collection item at index {index} is null. " +
                        "Null root items are not supported.");
                }

                itemType = itemType ?? item.GetType();

                FlatObject flatObject =
                    new ObjectFlattener(effectiveOptions).Flatten(item);

                Dictionary<string, object> rowValues =
                    flatObject.Values.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal);

                foreach (string key in rowValues.Keys)
                {
                    columns.Add(key);
                }

                rows.Add(new FlatRow(index, rowValues));
                index++;
            }

            if (itemType == null)
            {
                itemType = TypeUtility.TryGetEnumerableItemType(collection.GetType())
                           ?? typeof(object);
            }

            List<FlatRow> normalizedRows = new List<FlatRow>(rows.Count);

            foreach (FlatRow row in rows)
            {
                Dictionary<string, object> normalized =
                    new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (string column in columns)
                {
                    object value;
                    normalized[column] = row.Values.TryGetValue(column, out value)
                        ? value
                        : null;
                }

                normalizedRows.Add(new FlatRow(row.Index, normalized));
            }

            return new FlatTable(itemType, columns, normalizedRows);
        }

        public static List<T> DeserializeCollection<T>(
            FlatTable table,
            UnflattenOptions options = null)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            UnflattenOptions effectiveOptions =
                options ?? new UnflattenOptions();

            ObjectUnflattener unflattener =
                new ObjectUnflattener(effectiveOptions);

            return table.Rows
                .OrderBy(row => row.Index)
                .Select(row =>
                    (T)unflattener.Unflatten(typeof(T), row.Values))
                .ToList();
        }
    }
}
