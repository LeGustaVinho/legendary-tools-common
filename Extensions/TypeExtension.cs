﻿using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools
{
    public static class TypeExtension
    {
        private static Assembly[] allAssembliesCached = null;
        
        public static Type[] GetAllTypes(Func<Type, bool> filter, bool forceAssembliesRescan = false)
        {
            if (forceAssembliesRescan || allAssembliesCached == null)
            {
                allAssembliesCached = AppDomain.CurrentDomain.GetAssemblies();
            }
            
            return allAssembliesCached.SelectMany(assembly => assembly.GetTypes()).Where(filter).ToArray();
        }

        public static Type FindType(string typeName, bool forceAssembliesRescan = false)
        {
            if (string.IsNullOrEmpty(typeName)) return default;
            Type type = Type.GetType(typeName);

            if (type == null) 
            {
                if (forceAssembliesRescan || allAssembliesCached == null)
                    allAssembliesCached = AppDomain.CurrentDomain.GetAssemblies();

                foreach (Assembly assembly in allAssembliesCached)
                {
                    type = assembly.GetType(typeName);
                    if (type != null) break;
                    
                    type = assembly.GetTypes()
                        .FirstOrDefault(t => 
                            t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                            t.Name.StartsWith($"{typeName}`"));

                    if (type != null) break;
                }
            }

            if (type == null) throw new TypeLoadException($"[TypeExtension:FindType] '{typeName}' não foi encontrado.");

            return type;
        }
        
        public static bool IsSameOrSubclass(this Type potentialDescendant, Type potentialBase)
        {
            return potentialDescendant.IsSubclassOf(potentialBase)
                   || potentialDescendant == potentialBase;
        }

        public static bool IsFloatFamily(this Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        }

        public static bool IsIntFamily(this Type type)
        {
            return type == typeof(short) || type == typeof(int) || type == typeof(long) ||
                   type == typeof(ushort) || type == typeof(uint) || type == typeof(long);
        }

        public static bool IsNonStringEnumerable(this Type type)
        {
            if (type == null || type == typeof(string))
            {
                return false;
            }

            return typeof(IEnumerable).IsAssignableFrom(type);
        }

        public static bool IsBasicType(this Type type)
        {
            if (type == typeof(bool) || type == typeof(byte) || type == typeof(char) || type == typeof(decimal) ||
                type == typeof(double) || type.IsEnum
                || type == typeof(float) || type == typeof(int) || type == typeof(long) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(uint)
                || type == typeof(ulong) || type == typeof(ushort) || type == typeof(string) ||
                type == typeof(DateTime))
            {
                return true;
            }

            return false;
        }

        public static bool IsUnityBasicType(this Type type)
        {
            if (type == typeof(AnimationCurve) || type == typeof(Bounds) || type == typeof(Color) ||
                type == typeof(Color32) || type == typeof(Quaternion) || type.IsEnum
                || type == typeof(Rect) || type == typeof(Vector2) || type == typeof(Vector3) ||
                type == typeof(Vector4) || type == typeof(Matrix4x4) || type == typeof(LayerMask)
                || type == typeof(Gradient) || type == typeof(RectOffset) || type == typeof(GUIStyle))
            {
                return true;
            }

            return false;
        }

        public static bool IsStruct(this Type type)
        {
            return type.IsValueType && !type.IsEnum;
        }

        public static bool IsUnityObject(this Type type)
        {
            return type.IsSameOrSubclass(typeof(Object));
        }

        public static bool IsNullable(this Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return true;
            }

            if (Nullable.GetUnderlyingType(type) != null)
            {
                return true;
            }

            if (type.IsClass)
            {
                return true;
            }

            if (!type.IsValueType)
            {
                return true;
            }

            return false;
        }

        public static bool HasDefaultConstructor(this Type type)
        {
            return type.GetConstructor(Type.EmptyTypes) != null;
        }

        public static bool HasConstructorForTypes(this Type type, params Type[] types)
        {
            return type.GetConstructor(types) != null;
        }

        public static object Default(this Type type)
        {
            if (type.IsBasicType())
            {
                if (type == typeof(bool))
                {
                    return false;
                }

                if (type == typeof(byte))
                {
                    return 0;
                }

                if (type == typeof(char))
                {
                    return '\0';
                }

                if (type == typeof(decimal))
                {
                    return 0.0M;
                }

                if (type == typeof(double))
                {
                    return 0.0D;
                }

                if (type.IsEnum)
                {
                    return Enum.ToObject(type, 0);
                }

                if (type == typeof(float))
                {
                    return 0.0f;
                }

                if (type == typeof(int))
                {
                    return 0;
                }

                if (type == typeof(long))
                {
                    return 0L;
                }

                if (type == typeof(sbyte))
                {
                    return 0;
                }

                if (type == typeof(short))
                {
                    return 0;
                }

                if (type == typeof(uint))
                {
                    return 0;
                }

                if (type == typeof(ulong))
                {
                    return 0;
                }

                if (type == typeof(ushort))
                {
                    return 0;
                }

                if (type == typeof(string))
                {
                    return string.Empty;
                }

                if (type == typeof(DateTime))
                {
                    return DateTime.MinValue;
                }

                return default;
            }

            return null;
        }

        public static bool HasAttribute(this Type type, Type attributeType, bool inherit)
        {
            return type.GetCustomAttributes(attributeType, inherit).Length > 0;
        }
        
        public static T GetAttribute<T>(this Type type, bool inherit = true) where T : Attribute
        {
            return (T)Attribute.GetCustomAttribute(type, typeof(T), inherit);
        }

        public static bool CanBeSerializedByUnity(this Type type)
        {
            if (type.IsAbstract)
            {
                return false;
            }

            if (type == typeof(DateTime)) //because DateTime is primitive type and unity cant serialize
            {
                return true;
            }

            if (type.IsPrimitive)
            {
                return true;
            }

            if (type.IsEnum)
            {
                return true;
            }

            if (type.IsUnityBasicType())
            {
                return true;
            }

            if (type.IsArray)
            {
                return type.GetElementType().CanBeSerializedByUnity();
            }

            if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    return type.GetGenericArguments()[0].CanBeSerializedByUnity();
                }

                return false;
            }

            if (type.IsClass)
            {
                if (type.IsUnityObject())
                {
                    return true;
                }

                return type.HasAttribute(typeof(SerializableAttribute), false);
            }

            if (type.IsStruct())
            {
                return type.HasAttribute(typeof(SerializableAttribute), false);
            }

            return false;
        }

        public static bool Implements(this Type type, Type interfaces)
        {
            return type.GetInterfaces().Any(item => item == interfaces);
        }
    }
}