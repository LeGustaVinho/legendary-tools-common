using System;
using System.Collections.Generic;
using System.Reflection;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Unity-style property path parser/walker that supports:
    /// - Field names
    /// - "Array.size"
    /// - "Array.data[x]"
    /// - Nested fields
    /// Produces a walk result that references the container owner and field info, and whether it targets an element.
    /// </summary>
    public static class PropertyPath
    {
        public enum Kind
        {
            Field,
            Array,
            ArraySize,
            ArrayDataIndex
        }

        public readonly struct Token
        {
            public readonly Kind T;
            public readonly string Name;
            public readonly int Index;

            public Token(Kind t, string name, int index)
            {
                T = t;
                Name = name;
                Index = index;
            }
        }

        public readonly struct WalkResult
        {
            public readonly object ContainerOwner;
            public readonly FieldInfo Field;
            public readonly bool IsCollectionElement;
            public readonly int ElementIndex;
            public readonly bool IsArraySize;

            public WalkResult(object owner, FieldInfo field, bool isElem, int elemIndex, bool isSize)
            {
                ContainerOwner = owner;
                Field = field;
                IsCollectionElement = isElem;
                ElementIndex = elemIndex;
                IsArraySize = isSize;
            }
        }

        /// <summary>Parses a Unity-style property path into tokens.</summary>
        public static List<Token> Parse(string path)
        {
            List<Token> result = new();
            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];

                if (p == "Array")
                {
                    if (i + 1 < parts.Length)
                    {
                        string next = parts[i + 1];
                        if (next == "size")
                        {
                            result.Add(new Token(Kind.Array, "", -1));
                            result.Add(new Token(Kind.ArraySize, "", -1));
                            i++;
                            continue;
                        }

                        if (next.StartsWith("data["))
                        {
                            int open = next.IndexOf('[');
                            int close = next.IndexOf(']');
                            int idx = int.Parse(next.Substring(open + 1, close - open - 1));
                            result.Add(new Token(Kind.Array, "", -1));
                            result.Add(new Token(Kind.ArrayDataIndex, "", idx));
                            i++;
                            continue;
                        }
                    }

                    result.Add(new Token(Kind.Array, "", -1));
                    continue;
                }

                if (p.StartsWith("data["))
                {
                    int open = p.IndexOf('[');
                    int close = p.IndexOf(']');
                    int idx = int.Parse(p.Substring(open + 1, close - open - 1));
                    result.Add(new Token(Kind.ArrayDataIndex, "", idx));
                    continue;
                }

                if (p == "size")
                {
                    result.Add(new Token(Kind.ArraySize, "", -1));
                    continue;
                }

                result.Add(new Token(Kind.Field, p, -1));
            }

            return result;
        }

        /// <summary>Walks the path on a root object and returns the container owner/field context.</summary>
        public static WalkResult Walk(object root, List<Token> tokens)
        {
            object containerOwner = root;
            FieldInfo currentField = null;
            object value = root;

            bool isElement = false;
            int elementIndex = -1;
            bool isArraySize = false;

            for (int i = 0; i < tokens.Count; i++)
            {
                Token tk = tokens[i];
                switch (tk.T)
                {
                    case Kind.Field:
                    {
                        currentField = GetField(value.GetType(), tk.Name);
                        containerOwner = value;
                        value = currentField.GetValue(value);
                        isElement = false;
                        elementIndex = -1;
                        isArraySize = false;
                        break;
                    }

                    case Kind.Array:
                    {
                        // marker only
                        break;
                    }

                    case Kind.ArraySize:
                    {
                        isArraySize = true;
                        isElement = false;
                        elementIndex = -1;
                        break;
                    }

                    case Kind.ArrayDataIndex:
                    {
                        isArraySize = false;
                        isElement = true;
                        elementIndex = tk.Index;
                        break;
                    }
                }
            }

            return new WalkResult(containerOwner, currentField, isElement, elementIndex, isArraySize);
        }

        /// <summary>Gets a FieldInfo by name (public or non-public instance).</summary>
        public static FieldInfo GetField(Type t, string name)
        {
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo fi = t.GetField(name, BF);
            if (fi == null)
                throw new MissingFieldException($"Field '{name}' not found on type '{t.Name}'.");
            return fi;
        }

        /// <summary>Determines whether the field is an array.</summary>
        public static bool IsArray(FieldInfo f)
        {
            return f.FieldType.IsArray;
        }

        /// <summary>Determines whether the field is a List<T>.</summary>
        public static bool IsList(FieldInfo f)
        {
            return f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>);
        }
    }
}