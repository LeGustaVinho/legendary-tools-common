using System;
using System.Collections.Generic;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    internal static class ReflectionMembers
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;

        private static readonly object CacheLock = new object();

        private static readonly Dictionary<MemberLookupKey, FieldInfo> FieldCache =
            new Dictionary<MemberLookupKey, FieldInfo>();

        private static readonly Dictionary<MemberLookupKey, PropertyInfo> PropertyCache =
            new Dictionary<MemberLookupKey, PropertyInfo>();

        private static readonly Dictionary<MemberLookupKey, EventInfo> EventCache =
            new Dictionary<MemberLookupKey, EventInfo>();

        private static readonly Dictionary<MethodGroupLookupKey, MethodInfo[]> MethodGroupCache =
            new Dictionary<MethodGroupLookupKey, MethodInfo[]>();

        private static readonly Dictionary<Type, ConstructorInfo[]> ConstructorCache =
            new Dictionary<Type, ConstructorInfo[]>();

        private static readonly Dictionary<MethodOverloadResolutionKey, CachedExecutableResolution> MethodOverloadCache =
            new Dictionary<MethodOverloadResolutionKey, CachedExecutableResolution>();

        private static readonly Dictionary<ConstructorOverloadResolutionKey, CachedExecutableResolution> ConstructorOverloadCache =
            new Dictionary<ConstructorOverloadResolutionKey, CachedExecutableResolution>();

        public static void ClearCaches()
        {
            lock (CacheLock)
            {
                FieldCache.Clear();
                PropertyCache.Clear();
                EventCache.Clear();
                MethodGroupCache.Clear();
                ConstructorCache.Clear();
                MethodOverloadCache.Clear();
                ConstructorOverloadCache.Clear();
            }
        }

        public static FieldInfo GetField(Type type, string name, bool isStatic)
        {
            var key = new MemberLookupKey(type, name, isStatic);

            lock (CacheLock)
            {
                if (FieldCache.TryGetValue(key, out FieldInfo cachedField))
                {
                    return cachedField;
                }
            }

            FieldInfo field = type.GetField(name, isStatic ? StaticFlags : InstanceFlags);

            lock (CacheLock)
            {
                FieldCache[key] = field;
            }

            return field;
        }

        public static PropertyInfo GetProperty(Type type, string name, bool isStatic)
        {
            var key = new MemberLookupKey(type, name, isStatic);

            lock (CacheLock)
            {
                if (PropertyCache.TryGetValue(key, out PropertyInfo cachedProperty))
                {
                    return cachedProperty;
                }
            }

            PropertyInfo property = type.GetProperty(name, isStatic ? StaticFlags : InstanceFlags);

            if (property != null && property.GetIndexParameters().Length > 0)
            {
                property = null;
            }

            lock (CacheLock)
            {
                PropertyCache[key] = property;
            }

            return property;
        }

        public static EventInfo GetEvent(Type type, string name, bool isStatic)
        {
            var key = new MemberLookupKey(type, name, isStatic);

            lock (CacheLock)
            {
                if (EventCache.TryGetValue(key, out EventInfo cachedEvent))
                {
                    return cachedEvent;
                }
            }

            EventInfo eventInfo = type.GetEvent(name, isStatic ? StaticFlags : InstanceFlags);

            lock (CacheLock)
            {
                EventCache[key] = eventInfo;
            }

            return eventInfo;
        }

        public static PropertyInfo GetIndexer(Type type, TypeAccessPolicy accessPolicy)
        {
            PropertyInfo[] properties = type.GetProperties(InstanceFlags);

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                ParameterInfo[] indexParameters = property.GetIndexParameters();

                if (indexParameters.Length != 1)
                {
                    continue;
                }

                if (!accessPolicy.IsAllowed(property.PropertyType) ||
                    !accessPolicy.IsAllowed(indexParameters[0].ParameterType))
                {
                    continue;
                }

                return property;
            }

            return null;
        }

        public static RuntimeValue CreateInstance(Type type, string typeName, object[] arguments, TypeAccessPolicy accessPolicy)
        {
            accessPolicy.ThrowIfDenied(type, $"create instance of '{typeName}'");

            if (type.IsAbstract)
            {
                throw new ScriptException($"Cannot create an instance of abstract type '{typeName}'.");
            }

            if (type.IsInterface)
            {
                throw new ScriptException($"Cannot create an instance of interface type '{typeName}'.");
            }

            if (type.IsValueType && arguments.Length == 0)
            {
                object defaultInstance = Activator.CreateInstance(type);
                accessPolicy.ThrowIfDenied(defaultInstance.GetType(), $"create instance of '{typeName}'");
                return new RuntimeValue(defaultInstance, type);
            }

            ConstructorInfo[] constructors = GetConstructors(type);
            var overloadKey = new ConstructorOverloadResolutionKey(type, BuildArgumentSignature(arguments));

            MethodBase constructor = SelectBestExecutable(
                constructors,
                arguments,
                $"constructor for '{typeName}'",
                accessPolicy,
                overloadKey,
                out object[] convertedArguments);

            if (constructor == null)
            {
                throw new ScriptException($"No public constructor for '{typeName}' matches the provided arguments.");
            }

            try
            {
                object instance = ((ConstructorInfo)constructor).Invoke(convertedArguments);

                accessPolicy.ThrowIfDenied(instance.GetType(), $"create instance of '{typeName}'");

                return new RuntimeValue(instance, type);
            }
            catch (TargetInvocationException exception)
            {
                string message = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
                throw new ScriptException($"Constructor for '{typeName}' threw an exception: {message}");
            }
        }

        public static RuntimeValue InvokeBestMethod(
            Type targetType,
            object targetObject,
            string methodName,
            object[] arguments,
            bool isStatic,
            TypeAccessPolicy accessPolicy)
        {
            accessPolicy.ThrowIfDenied(targetType, $"call method '{methodName}' on '{targetType.Name}'");

            MethodInfo[] matchingMethods = GetMatchingMethods(targetType, methodName, isStatic);
            var overloadKey = new MethodOverloadResolutionKey(targetType, methodName, isStatic, BuildArgumentSignature(arguments));

            MethodBase selectedMethod = SelectBestExecutable(
                matchingMethods,
                arguments,
                $"method '{methodName}' on '{targetType.Name}'",
                accessPolicy,
                overloadKey,
                out object[] convertedArguments);

            if (selectedMethod == null)
            {
                string targetKind = isStatic ? "static" : "instance";
                throw new ScriptException(
                    $"No public {targetKind} method '{methodName}' on '{targetType.Name}' matches the provided arguments.");
            }

            try
            {
                var method = (MethodInfo)selectedMethod;

                accessPolicy.ThrowIfDenied(method.ReturnType, $"call method '{methodName}' return type");

                object result = method.Invoke(targetObject, convertedArguments);

                if (method.ReturnType == typeof(void))
                {
                    return new RuntimeValue(null, typeof(void));
                }

                if (result != null)
                {
                    accessPolicy.ThrowIfDenied(result.GetType(), $"call method '{methodName}' result");
                }

                return new RuntimeValue(result, method.ReturnType);
            }
            catch (TargetInvocationException exception)
            {
                string message = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
                throw new ScriptException($"Method '{methodName}' on '{targetType.Name}' threw an exception: {message}");
            }
        }

        private static ConstructorInfo[] GetConstructors(Type type)
        {
            lock (CacheLock)
            {
                if (ConstructorCache.TryGetValue(type, out ConstructorInfo[] cachedConstructors))
                {
                    return cachedConstructors;
                }
            }

            ConstructorInfo[] constructors = type.GetConstructors(InstanceFlags);

            lock (CacheLock)
            {
                ConstructorCache[type] = constructors;
            }

            return constructors;
        }

        private static MethodInfo[] GetMatchingMethods(Type targetType, string methodName, bool isStatic)
        {
            var key = new MethodGroupLookupKey(targetType, methodName, isStatic);

            lock (CacheLock)
            {
                if (MethodGroupCache.TryGetValue(key, out MethodInfo[] cachedMethods))
                {
                    return cachedMethods;
                }
            }

            BindingFlags flags = isStatic ? StaticFlags : InstanceFlags;
            MethodInfo[] allMethods = targetType.GetMethods(flags);
            var matchingMethods = new List<MethodInfo>();

            for (int i = 0; i < allMethods.Length; i++)
            {
                MethodInfo method = allMethods[i];

                if (method.Name != methodName)
                {
                    continue;
                }

                if (method.IsGenericMethodDefinition)
                {
                    continue;
                }

                matchingMethods.Add(method);
            }

            MethodInfo[] result = matchingMethods.ToArray();

            lock (CacheLock)
            {
                MethodGroupCache[key] = result;
            }

            return result;
        }

        private static MethodBase SelectBestExecutable(
            MethodBase[] executables,
            object[] arguments,
            string description,
            TypeAccessPolicy accessPolicy,
            object overloadCacheKey,
            out object[] convertedArguments)
        {
            if (TryGetCachedResolution(overloadCacheKey, out CachedExecutableResolution cachedResolution))
            {
                if (cachedResolution.IsAmbiguous)
                {
                    throw new ScriptException($"Ambiguous overload resolution for {description}.");
                }

                if (cachedResolution.Executable == null)
                {
                    convertedArguments = null;
                    return null;
                }

                convertedArguments = RuntimeConversion.ConvertArguments(arguments, cachedResolution.Executable.GetParameters());
                return cachedResolution.Executable;
            }

            MethodBase bestExecutable = null;
            int bestScore = int.MaxValue;
            bool ambiguous = false;

            for (int i = 0; i < executables.Length; i++)
            {
                MethodBase executable = executables[i];

                if (!TryScoreArguments(executable.GetParameters(), arguments, accessPolicy, out int candidateScore))
                {
                    continue;
                }

                if (candidateScore < bestScore)
                {
                    bestExecutable = executable;
                    bestScore = candidateScore;
                    ambiguous = false;
                }
                else if (candidateScore == bestScore)
                {
                    ambiguous = true;
                }
            }

            if (ambiguous)
            {
                CacheResolution(overloadCacheKey, CachedExecutableResolution.Ambiguous());
                throw new ScriptException($"Ambiguous overload resolution for {description}.");
            }

            if (bestExecutable == null)
            {
                CacheResolution(overloadCacheKey, CachedExecutableResolution.NotFound());
                convertedArguments = null;
                return null;
            }

            CacheResolution(overloadCacheKey, CachedExecutableResolution.Found(bestExecutable));
            convertedArguments = RuntimeConversion.ConvertArguments(arguments, bestExecutable.GetParameters());
            return bestExecutable;
        }

        private static ArgumentSignature BuildArgumentSignature(object[] arguments)
        {
            Type[] argumentTypes = new Type[arguments.Length];

            for (int i = 0; i < arguments.Length; i++)
            {
                argumentTypes[i] = arguments[i]?.GetType();
            }

            return new ArgumentSignature(argumentTypes);
        }

        private static bool TryGetCachedResolution(object key, out CachedExecutableResolution resolution)
        {
            lock (CacheLock)
            {
                if (key is MethodOverloadResolutionKey methodKey)
                {
                    return MethodOverloadCache.TryGetValue(methodKey, out resolution);
                }

                if (key is ConstructorOverloadResolutionKey constructorKey)
                {
                    return ConstructorOverloadCache.TryGetValue(constructorKey, out resolution);
                }
            }

            resolution = default;
            return false;
        }

        private static void CacheResolution(object key, CachedExecutableResolution resolution)
        {
            lock (CacheLock)
            {
                if (key is MethodOverloadResolutionKey methodKey)
                {
                    MethodOverloadCache[methodKey] = resolution;
                }
                else if (key is ConstructorOverloadResolutionKey constructorKey)
                {
                    ConstructorOverloadCache[constructorKey] = resolution;
                }
            }
        }

        private static bool TryScoreArguments(
            ParameterInfo[] parameters,
            object[] arguments,
            TypeAccessPolicy accessPolicy,
            out int score)
        {
            score = 0;

            if (parameters.Length != arguments.Length)
            {
                return false;
            }

            for (int i = 0; i < arguments.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;

                if (parameterType.IsByRef)
                {
                    return false;
                }

                if (!accessPolicy.IsAllowed(parameterType))
                {
                    return false;
                }

                if (arguments[i] != null && !accessPolicy.IsAllowed(arguments[i].GetType()))
                {
                    return false;
                }

                if (!RuntimeConversion.CanConvertTo(arguments[i], parameterType, out int conversionScore))
                {
                    return false;
                }

                score += conversionScore;
            }

            return true;
        }

        private readonly struct MemberLookupKey : IEquatable<MemberLookupKey>
        {
            private readonly Type _type;
            private readonly string _name;
            private readonly bool _isStatic;

            public MemberLookupKey(Type type, string name, bool isStatic)
            {
                _type = type;
                _name = name;
                _isStatic = isStatic;
            }

            public bool Equals(MemberLookupKey other)
            {
                return _type == other._type &&
                       _isStatic == other._isStatic &&
                       string.Equals(_name, other._name, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is MemberLookupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (_type != null ? _type.GetHashCode() : 0);
                    hash = hash * 31 + (_name != null ? StringComparer.Ordinal.GetHashCode(_name) : 0);
                    hash = hash * 31 + (_isStatic ? 1 : 0);
                    return hash;
                }
            }
        }

        private readonly struct MethodGroupLookupKey : IEquatable<MethodGroupLookupKey>
        {
            private readonly Type _type;
            private readonly string _methodName;
            private readonly bool _isStatic;

            public MethodGroupLookupKey(Type type, string methodName, bool isStatic)
            {
                _type = type;
                _methodName = methodName;
                _isStatic = isStatic;
            }

            public bool Equals(MethodGroupLookupKey other)
            {
                return _type == other._type &&
                       _isStatic == other._isStatic &&
                       string.Equals(_methodName, other._methodName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is MethodGroupLookupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (_type != null ? _type.GetHashCode() : 0);
                    hash = hash * 31 + (_methodName != null ? StringComparer.Ordinal.GetHashCode(_methodName) : 0);
                    hash = hash * 31 + (_isStatic ? 1 : 0);
                    return hash;
                }
            }
        }

        private readonly struct ArgumentSignature : IEquatable<ArgumentSignature>
        {
            private readonly Type[] _argumentTypes;

            public ArgumentSignature(Type[] argumentTypes)
            {
                _argumentTypes = argumentTypes;
            }

            public bool Equals(ArgumentSignature other)
            {
                if (_argumentTypes == null || other._argumentTypes == null)
                {
                    return _argumentTypes == other._argumentTypes;
                }

                if (_argumentTypes.Length != other._argumentTypes.Length)
                {
                    return false;
                }

                for (int i = 0; i < _argumentTypes.Length; i++)
                {
                    if (_argumentTypes[i] != other._argumentTypes[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return obj is ArgumentSignature other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;

                    if (_argumentTypes == null)
                    {
                        return hash;
                    }

                    for (int i = 0; i < _argumentTypes.Length; i++)
                    {
                        hash = hash * 31 + (_argumentTypes[i] != null ? _argumentTypes[i].GetHashCode() : 0);
                    }

                    return hash;
                }
            }
        }

        private readonly struct MethodOverloadResolutionKey : IEquatable<MethodOverloadResolutionKey>
        {
            private readonly Type _targetType;
            private readonly string _methodName;
            private readonly bool _isStatic;
            private readonly ArgumentSignature _argumentSignature;

            public MethodOverloadResolutionKey(Type targetType, string methodName, bool isStatic, ArgumentSignature argumentSignature)
            {
                _targetType = targetType;
                _methodName = methodName;
                _isStatic = isStatic;
                _argumentSignature = argumentSignature;
            }

            public bool Equals(MethodOverloadResolutionKey other)
            {
                return _targetType == other._targetType &&
                       _isStatic == other._isStatic &&
                       _argumentSignature.Equals(other._argumentSignature) &&
                       string.Equals(_methodName, other._methodName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is MethodOverloadResolutionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (_targetType != null ? _targetType.GetHashCode() : 0);
                    hash = hash * 31 + (_methodName != null ? StringComparer.Ordinal.GetHashCode(_methodName) : 0);
                    hash = hash * 31 + (_isStatic ? 1 : 0);
                    hash = hash * 31 + _argumentSignature.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct ConstructorOverloadResolutionKey : IEquatable<ConstructorOverloadResolutionKey>
        {
            private readonly Type _targetType;
            private readonly ArgumentSignature _argumentSignature;

            public ConstructorOverloadResolutionKey(Type targetType, ArgumentSignature argumentSignature)
            {
                _targetType = targetType;
                _argumentSignature = argumentSignature;
            }

            public bool Equals(ConstructorOverloadResolutionKey other)
            {
                return _targetType == other._targetType &&
                       _argumentSignature.Equals(other._argumentSignature);
            }

            public override bool Equals(object obj)
            {
                return obj is ConstructorOverloadResolutionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (_targetType != null ? _targetType.GetHashCode() : 0);
                    hash = hash * 31 + _argumentSignature.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct CachedExecutableResolution
        {
            private CachedExecutableResolution(MethodBase executable, bool isAmbiguous)
            {
                Executable = executable;
                IsAmbiguous = isAmbiguous;
            }

            public MethodBase Executable { get; }

            public bool IsAmbiguous { get; }

            public static CachedExecutableResolution Found(MethodBase executable)
            {
                return new CachedExecutableResolution(executable, false);
            }

            public static CachedExecutableResolution NotFound()
            {
                return new CachedExecutableResolution(null, false);
            }

            public static CachedExecutableResolution Ambiguous()
            {
                return new CachedExecutableResolution(null, true);
            }
        }
    }
}
