using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Runtime resolver that produces a concrete clone of the base ScriptableObject and applies overridden values
    /// from the variant payload using Unity-style property paths.
    /// Supports arrays and List<T>. Supports SerializeReference via shallow copy.
    /// </summary>
    public static class VariantResolver
    {
        /// <summary>Resolves a clone of T (base + overrides).</summary>
        public static T Resolve<T>(ScriptableObjectVariant variant) where T : ScriptableObject
        {
            T result = ScriptableObject.CreateInstance(variant.BaseAsset.GetType()) as T;
            ApplyBaseThenOverrides(variant, result);
            return result;
        }

        /// <summary>Resolves a clone (base + overrides) for a runtime-provided type.</summary>
        public static ScriptableObject Resolve(ScriptableObjectVariant variant, Type concreteType)
        {
            ScriptableObject result = ScriptableObject.CreateInstance(concreteType);
            ApplyBaseThenOverrides(variant, result);
            return result;
        }

        /// <summary>
        /// Copies the whole base to destination, then applies overrides from the payload.
        /// Array/List sizes are handled first (paths ending with ".Array.size"), then values/elements.
        /// </summary>
        private static void ApplyBaseThenOverrides(ScriptableObjectVariant variant, ScriptableObject dst)
        {
            DeepCopySerializedFields(variant.BaseAsset, dst);

            List<string> sizePaths = new();
            List<string> valuePaths = new();
            foreach (string p in variant.Overrides.Enumerate())
            {
                if (p.EndsWith(".Array.size", StringComparison.Ordinal))
                    sizePaths.Add(p);
                else
                    valuePaths.Add(p);
            }

            foreach (string p in sizePaths)
            {
                ApplyArraySizeFromPayload(variant.Payload, dst, p);
            }

            foreach (string p in valuePaths)
            {
                CopyValueAtPath(variant.Payload, dst, p);
            }
        }

        /// <summary>Applies Array/List size and copies existing payload elements to destination to keep parity.</summary>
        private static void ApplyArraySizeFromPayload(object srcRoot, object dstRoot, string propertyPath)
        {
            List<PropertyPath.Token> tokens = PropertyPath.Parse(propertyPath);
            PropertyPath.WalkResult wrDst = PropertyPath.Walk(dstRoot, tokens);
            PropertyPath.WalkResult wrSrc = PropertyPath.Walk(srcRoot, tokens);

            if (!wrDst.IsArraySize || wrDst.Field == null)
                return;

            object dstContainer = wrDst.ContainerOwner;
            object srcContainer = wrSrc.ContainerOwner;
            FieldInfo field = wrDst.Field;
            Type fieldType = field.FieldType;

            if (PropertyPath.IsArray(field))
            {
                Array srcArr = (Array)wrSrc.Field.GetValue(srcContainer);
                Type elemType = fieldType.GetElementType();
                int len = srcArr?.Length ?? 0;

                Array newArr = Array.CreateInstance(elemType!, len);
                for (int i = 0; i < len; i++)
                {
                    object v = srcArr.GetValue(i);
                    newArr.SetValue(CloneValue(v, elemType!), i);
                }

                field.SetValue(dstContainer, newArr);
            }
            else if (PropertyPath.IsList(field))
            {
                IList srcList = (IList)wrSrc.Field.GetValue(srcContainer);
                IList dstList = (IList)field.GetValue(dstContainer);
                if (dstList == null)
                {
                    dstList = (IList)Activator.CreateInstance(fieldType);
                    field.SetValue(dstContainer, dstList);
                }

                Type elemType = fieldType.GetGenericArguments()[0];
                int targetCount = srcList?.Count ?? 0;

                while (dstList.Count < targetCount)
                {
                    dstList.Add(Activator.CreateInstance(elemType));
                }

                while (dstList.Count > targetCount)
                {
                    dstList.RemoveAt(dstList.Count - 1);
                }

                for (int i = 0; i < targetCount; i++)
                {
                    dstList[i] = CloneValue(srcList[i], elemType);
                }
            }
        }

        /// <summary>Copies a single property's value from src payload to dst clone using the Unity-style path.</summary>
        private static void CopyValueAtPath(object srcRoot, object dstRoot, string propertyPath)
        {
            List<PropertyPath.Token> tokens = PropertyPath.Parse(propertyPath);
            PropertyPath.WalkResult wrDst = PropertyPath.Walk(dstRoot, tokens);
            PropertyPath.WalkResult wrSrc = PropertyPath.Walk(srcRoot, tokens);

            if (wrDst.Field == null)
                return;

            if (wrDst.IsArraySize)
                return; // handled earlier

            object dstContainer = wrDst.ContainerOwner;
            object srcContainer = wrSrc.ContainerOwner;

            if (wrDst.IsCollectionElement)
            {
                if (PropertyPath.IsArray(wrDst.Field))
                {
                    Array dstArr = (Array)wrDst.Field.GetValue(dstContainer);
                    Array srcArr = (Array)wrSrc.Field.GetValue(srcContainer);
                    if (dstArr == null || srcArr == null)
                        return;

                    Type elemType = dstArr.GetType().GetElementType();
                    int idx = wrDst.ElementIndex;
                    if (idx < 0 || idx >= dstArr.Length || idx >= srcArr.Length)
                        return;

                    object val = CloneValue(srcArr.GetValue(idx), elemType!);
                    dstArr.SetValue(val, idx);
                }
                else if (PropertyPath.IsList(wrDst.Field))
                {
                    IList dstList = (IList)wrDst.Field.GetValue(dstContainer);
                    IList srcList = (IList)wrSrc.Field.GetValue(srcContainer);
                    if (dstList == null || srcList == null)
                        return;

                    Type elemType = wrDst.Field.FieldType.GetGenericArguments()[0];
                    int idx = wrDst.ElementIndex;

                    while (dstList.Count <= idx)
                    {
                        dstList.Add(Activator.CreateInstance(elemType));
                    }

                    if (idx < srcList.Count)
                        dstList[idx] = CloneValue(srcList[idx], elemType);
                }

                return;
            }

            // Plain field
            object srcVal = wrSrc.Field.GetValue(srcContainer);

            // For managed references (SerializeReference), do shallow copy to preserve dynamic runtime type.
            if (Attribute.IsDefined(wrDst.Field, typeof(SerializeReference)))
            {
                wrDst.Field.SetValue(dstContainer, srcVal);
                return;
            }

            object cloned = CloneValue(srcVal, wrDst.Field.FieldType);
            wrDst.Field.SetValue(dstContainer, cloned);
        }

        /// <summary>Deep-copies Unity-serializable fields from src to dst. Uses shallow copy for [SerializeReference].</summary>
        private static void DeepCopySerializedFields(object src, object dst)
        {
            if (src == null || dst == null)
                return;

            Type type = dst.GetType();
            if (type != src.GetType())
                throw new InvalidOperationException("DeepCopySerializedFields requires equal concrete types.");

            foreach (FieldInfo f in EnumerateUnitySerializableFields(type))
            {
                object sv = f.GetValue(src);

                if (Attribute.IsDefined(f, typeof(SerializeReference)))
                {
                    // Preserve dynamic type by shallow copy.
                    f.SetValue(dst, sv);
                    continue;
                }

                object dv = CloneValue(sv, f.FieldType);
                f.SetValue(dst, dv);
            }
        }

        /// <summary>Clones a value compatible with Unity serialization (arrays, lists, structs/classes, UnityEngine.Object refs).</summary>
        private static object CloneValue(object value, Type t)
        {
            if (value == null)
                return null;

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return (UnityEngine.Object)value;

            if (t.IsPrimitive || t.IsEnum || t == typeof(string))
                return value;

            if (t.IsArray)
            {
                Type elemType = t.GetElementType();
                Array srcArray = (Array)value;
                Array dstArray = Array.CreateInstance(elemType!, srcArray.Length);
                for (int i = 0; i < srcArray.Length; i++)
                {
                    dstArray.SetValue(CloneValue(srcArray.GetValue(i), elemType!), i);
                }

                return dstArray;
            }

            if (IsGenericList(t))
            {
                Type elemType = t.GetGenericArguments()[0];
                IList list = (IList)value;
                IList dst = (IList)Activator.CreateInstance(t);
                for (int i = 0; i < list.Count; i++)
                {
                    dst.Add(CloneValue(list[i], elemType));
                }

                return dst;
            }

            object inst = Activator.CreateInstance(t);
            foreach (FieldInfo f in EnumerateUnitySerializableFields(t))
            {
                object fv = f.GetValue(value);

                if (Attribute.IsDefined(f, typeof(SerializeReference)))
                    f.SetValue(inst, fv); // shallow for managed reference
                else
                    f.SetValue(inst, CloneValue(fv, f.FieldType));
            }

            return inst;
        }

        private static bool IsGenericList(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
        }

        /// <summary>Enumerates Unity-serialized instance fields, including [SerializeReference].</summary>
        private static IEnumerable<FieldInfo> EnumerateUnitySerializableFields(Type t)
        {
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (FieldInfo f in t.GetFields(BF))
            {
                if (f.IsStatic)
                    continue;

                if (f.IsPublic)
                {
                    if (Attribute.IsDefined(f, typeof(NonSerializedAttribute)))
                        continue;

                    if (IsUnitySerializableField(f))
                        yield return f;

                    continue;
                }

                // Private/protected require [SerializeField] or [SerializeReference]
                if ((Attribute.IsDefined(f, typeof(SerializeField)) ||
                     Attribute.IsDefined(f, typeof(SerializeReference)))
                    && IsUnitySerializableField(f))
                    yield return f;
            }
        }

        /// <summary>Determines if a field is serializable by Unity rules, including SerializeReference.</summary>
        private static bool IsUnitySerializableField(FieldInfo f)
        {
            Type t = f.FieldType;

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return true;

            if (Attribute.IsDefined(f, typeof(SerializeReference)))
                return true; // allow any managed ref

            if (t.IsPrimitive || t.IsEnum || t == typeof(string))
                return true;

            if (t.IsArray)
                return IsUnitySerializableType(t.GetElementType());

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return IsUnitySerializableType(t.GetGenericArguments()[0]);

            return Attribute.IsDefined(t, typeof(SerializableAttribute));
        }

        /// <summary>Type helper for elements/containers (not field-level).</summary>
        private static bool IsUnitySerializableType(Type t)
        {
            if (t.IsPrimitive || t.IsEnum || t == typeof(string))
                return true;

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return true;

            if (t.IsArray)
                return IsUnitySerializableType(t.GetElementType());

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return IsUnitySerializableType(t.GetGenericArguments()[0]);

            return Attribute.IsDefined(t, typeof(SerializableAttribute));
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor helper that copies all serialized fields from src to dst
        /// (deep copy, shallow for SerializeReference).
        /// </summary>
        public static void __EDITOR_CopyAll(object src, object dst)
        {
            if (src == null || dst == null)
                return;

            if (src.GetType() != dst.GetType())
                return;

            Type t = src.GetType();
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (FieldInfo f in t.GetFields(BF))
            {
                if (f.IsStatic)
                    continue;

                // Respect Unity's serialized surface
                if (f.IsPublic && Attribute.IsDefined(f, typeof(NonSerializedAttribute)))
                    continue;

                bool eligible =
                    f.IsPublic
                    || Attribute.IsDefined(f, typeof(SerializeField))
                    || Attribute.IsDefined(f, typeof(SerializeReference));

                if (!eligible)
                    continue;

                if (!IsUnitySerializableField(f))
                    continue;

                object v = f.GetValue(src);

                if (Attribute.IsDefined(f, typeof(SerializeReference)))
                    f.SetValue(dst, v); // shallow
                else
                    f.SetValue(dst, CloneValue(v, f.FieldType));
            }
        }
#endif
    }
}