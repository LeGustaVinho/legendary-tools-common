using System;
using System.Reflection;
using System.Threading.Tasks;

namespace LegendaryTools.ViewBinding
{
    public static class TaskMethodBindingUtility
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags StaticFlags =
            BindingFlags.Static | BindingFlags.Public;

        public static bool IsSupportedMethod(
            MethodInfo method,
            EventBindingActionParameterMode parameterMode,
            bool requireStatic)
        {
            if (method == null ||
                method.IsGenericMethodDefinition ||
                method.IsSpecialName ||
                method.ReturnType != typeof(Task) ||
                method.IsStatic != requireStatic)
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != GetParameterCount(parameterMode))
            {
                return false;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].IsOut || parameters[i].ParameterType.IsByRef)
                {
                    return false;
                }
            }

            return true;
        }

        public static string CreateSignature(MethodInfo method)
        {
            if (method == null)
            {
                return string.Empty;
            }

            ParameterInfo[] parameters = method.GetParameters();
            string signature = method.Name;
            for (int i = 0; i < parameters.Length; i++)
            {
                signature += "|" + parameters[i].ParameterType.AssemblyQualifiedName;
            }

            return signature;
        }

        public static string GetDisplaySignature(MethodInfo method)
        {
            if (method == null)
            {
                return "Select Task Method";
            }

            ParameterInfo[] parameters = method.GetParameters();
            string signature = method.Name + "(";
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    signature += ", ";
                }

                signature += GetFriendlyTypeName(parameters[i].ParameterType) + " " + parameters[i].Name;
            }

            return signature + ")";
        }

        public static bool TryResolveMethod(
            BindingInstanceHandle handle,
            string signature,
            EventBindingActionParameterMode parameterMode,
            out MethodInfo method,
            out string error)
        {
            method = null;

            if (handle.Type == null)
            {
                error = "The Task method target type is not resolved.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                error = "No Task method is selected.";
                return false;
            }

            BindingFlags flags = handle.IsStatic ? StaticFlags : InstanceFlags;
            MethodInfo[] methods = handle.Type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (IsSupportedMethod(candidate, parameterMode, handle.IsStatic) &&
                    string.Equals(CreateSignature(candidate), signature, StringComparison.Ordinal))
                {
                    method = candidate;
                    error = string.Empty;
                    return true;
                }
            }

            error = $"The selected Task method could not be resolved on '{handle.Type.FullName}'.";
            return false;
        }

        public static bool TryPrepareArguments(
            ParameterInfo[] parameters,
            EventBindingActionParameterMode parameterMode,
            object oldValue,
            object newValue,
            ref object[] arguments,
            out string error)
        {
            if (parameters == null)
            {
                arguments = null;
                error = "Task method parameter metadata is unavailable.";
                return false;
            }

            int parameterCount = parameters.Length;
            if (parameterCount == 0)
            {
                arguments = Array.Empty<object>();
                error = string.Empty;
                return true;
            }

            if (arguments == null || arguments.Length != parameterCount)
            {
                arguments = new object[parameterCount];
            }

            switch (parameterMode)
            {
                case EventBindingActionParameterMode.OldValue:
                    arguments[0] = oldValue;
                    break;

                case EventBindingActionParameterMode.NewValue:
                    arguments[0] = newValue;
                    break;

                case EventBindingActionParameterMode.OldAndNewValues:
                    arguments[0] = oldValue;
                    arguments[1] = newValue;
                    break;

                case EventBindingActionParameterMode.None:
                    break;

                default:
                    error = $"Unsupported action parameter mode: {parameterMode}.";
                    return false;
            }

            for (int i = 0; i < parameterCount; i++)
            {
                if (!TryValidateArgument(arguments[i], parameters[i].ParameterType))
                {
                    error = $"Argument {i + 1} is not assignable to '{parameters[i].ParameterType.FullName}'.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        public static int GetParameterCount(EventBindingActionParameterMode parameterMode)
        {
            return parameterMode == EventBindingActionParameterMode.OldAndNewValues
                ? 2
                : parameterMode == EventBindingActionParameterMode.None
                    ? 0
                    : 1;
        }

        private static bool TryValidateArgument(object value, Type parameterType)
        {
            if (value == null)
            {
                return !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null;
            }

            Type nullableType = Nullable.GetUnderlyingType(parameterType);
            return parameterType.IsInstanceOfType(value) ||
                   (nullableType != null && nullableType.IsInstanceOfType(value));
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.Name;
            }

            string name = type.Name;
            int markerIndex = name.IndexOf('`');
            if (markerIndex >= 0)
            {
                name = name.Substring(0, markerIndex);
            }

            Type[] arguments = type.GetGenericArguments();
            string result = name + "<";
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    result += ", ";
                }

                result += GetFriendlyTypeName(arguments[i]);
            }

            return result + ">";
        }
    }
}
