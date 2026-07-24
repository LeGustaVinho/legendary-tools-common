#if !ENABLE_IL2CPP
using System;
using System.Linq.Expressions;
using System.Reflection;
#endif

namespace LegendaryTools.ViewBinding
{
    internal static class BindingReflectionAccessorFactory
    {
#if !ENABLE_IL2CPP
        public static bool TryCreateGetter(
            System.Reflection.MemberInfo member,
            bool isStatic,
            out Func<object, object> getter)
        {
            getter = null;
            try
            {
                ParameterExpression instanceParameter = Expression.Parameter(typeof(object), "instance");
                Expression instance = isStatic
                    ? null
                    : Expression.Convert(instanceParameter, member.DeclaringType);
                Expression access = member is FieldInfo field
                    ? Expression.Field(instance, field)
                    : Expression.Property(instance, (PropertyInfo)member);
                getter = Expression.Lambda<Func<object, object>>(
                    Expression.Convert(access, typeof(object)),
                    instanceParameter).Compile();
                return true;
            }
            catch
            {
                getter = null;
                return false;
            }
        }

        public static bool TryCreateSetter(
            System.Reflection.MemberInfo member,
            bool isStatic,
            out Action<object, object> setter)
        {
            setter = null;
            if (!isStatic && member?.DeclaringType != null && member.DeclaringType.IsValueType)
            {
                return false;
            }

            try
            {
                Type valueType;
                if (member is FieldInfo field)
                {
                    if (field.IsInitOnly || field.IsLiteral)
                    {
                        return false;
                    }

                    valueType = field.FieldType;
                }
                else if (member is PropertyInfo property)
                {
                    if (property.GetSetMethod(false) == null)
                    {
                        return false;
                    }

                    valueType = property.PropertyType;
                }
                else
                {
                    return false;
                }

                ParameterExpression instanceParameter = Expression.Parameter(typeof(object), "instance");
                ParameterExpression valueParameter = Expression.Parameter(typeof(object), "value");
                Expression instance = isStatic
                    ? null
                    : Expression.Convert(instanceParameter, member.DeclaringType);
                Expression access = member is FieldInfo targetField
                    ? Expression.Field(instance, targetField)
                    : Expression.Property(instance, (PropertyInfo)member);
                BinaryExpression assignment = Expression.Assign(
                    access,
                    Expression.Convert(valueParameter, valueType));
                setter = Expression.Lambda<Action<object, object>>(
                    assignment,
                    instanceParameter,
                    valueParameter).Compile();
                return true;
            }
            catch
            {
                setter = null;
                return false;
            }
        }
#else
        public static bool TryCreateGetter(
            System.Reflection.MemberInfo member,
            bool isStatic,
            out System.Func<object, object> getter)
        {
            getter = null;
            return false;
        }

        public static bool TryCreateSetter(
            System.Reflection.MemberInfo member,
            bool isStatic,
            out System.Action<object, object> setter)
        {
            setter = null;
            return false;
        }
#endif
    }
}
