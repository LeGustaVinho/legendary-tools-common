using System;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class MemberExpression : Expression, IAssignableExpression
    {
        private readonly Expression _target;
        private readonly Token _memberName;

        public MemberExpression(Expression target, Token memberName)
        {
            _target = target;
            _memberName = memberName;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            return GetValue(context);
        }

        public RuntimeValue GetValue(ScriptContext context)
        {
            RuntimeValue targetValue = _target.Evaluate(context);
            ResolveMemberTarget(context, targetValue, out Type targetType, out object targetObject, out bool isStatic);

            FieldInfo field = ReflectionMembers.GetField(targetType, _memberName.Lexeme, isStatic);

            if (field != null)
            {
                context.EnsureTypeAllowed(field.FieldType, $"read field '{_memberName.Lexeme}'");

                object value = field.GetValue(targetObject);

                context.EnsureValueAllowed(value, $"read field '{_memberName.Lexeme}'");

                return new RuntimeValue(value, field.FieldType);
            }

            PropertyInfo property = ReflectionMembers.GetProperty(targetType, _memberName.Lexeme, isStatic);

            if (property != null && property.CanRead)
            {
                context.EnsureTypeAllowed(property.PropertyType, $"read property '{_memberName.Lexeme}'");

                object value = property.GetValue(targetObject, null);

                context.EnsureValueAllowed(value, $"read property '{_memberName.Lexeme}'");

                return new RuntimeValue(value, property.PropertyType);
            }

            string targetKind = isStatic ? "static" : "instance";
            throw new ScriptException(
                $"Readable public {targetKind} field or property '{_memberName.Lexeme}' was not found on '{targetType.Name}'.");
        }

        public void Assign(ScriptContext context, object value)
        {
            RuntimeValue targetValue = _target.Evaluate(context);
            ResolveMemberTarget(context, targetValue, out Type targetType, out object targetObject, out bool isStatic);

            context.EnsureValueAllowed(value, $"assign member '{_memberName.Lexeme}'");

            FieldInfo field = ReflectionMembers.GetField(targetType, _memberName.Lexeme, isStatic);

            if (field != null)
            {
                context.EnsureTypeAllowed(field.FieldType, $"assign field '{_memberName.Lexeme}'");

                field.SetValue(targetObject, RuntimeConversion.ConvertTo(value, field.FieldType));
                PersistValueTypeTarget(context, targetType, targetObject, isStatic);
                return;
            }

            PropertyInfo property = ReflectionMembers.GetProperty(targetType, _memberName.Lexeme, isStatic);

            if (property != null && property.CanWrite)
            {
                context.EnsureTypeAllowed(property.PropertyType, $"assign property '{_memberName.Lexeme}'");

                property.SetValue(targetObject, RuntimeConversion.ConvertTo(value, property.PropertyType), null);
                PersistValueTypeTarget(context, targetType, targetObject, isStatic);
                return;
            }

            string targetKind = isStatic ? "static" : "instance";
            throw new ScriptException(
                $"Writable public {targetKind} field or property '{_memberName.Lexeme}' was not found on '{targetType.Name}'.");
        }

        public RuntimeValue Call(ScriptContext context, object[] arguments)
        {
            RuntimeValue targetValue = _target.Evaluate(context);
            ResolveMemberTarget(context, targetValue, out Type targetType, out object targetObject, out bool isStatic);

            RuntimeValue result = ReflectionMembers.InvokeBestMethod(
                targetType,
                targetObject,
                _memberName.Lexeme,
                arguments,
                isStatic,
                context.AccessPolicy);

            PersistValueTypeTarget(context, targetType, targetObject, isStatic);

            context.EnsureTypeAllowed(result.Type, $"call method '{_memberName.Lexeme}'");
            context.EnsureValueAllowed(result.Value, $"call method '{_memberName.Lexeme}'");

            return result;
        }

        public bool TryApplyCompoundAssignment(
            ScriptContext context,
            TokenType operatorType,
            Expression valueExpression,
            out RuntimeValue result)
        {
            result = default;

            if (operatorType != TokenType.PlusEqual && operatorType != TokenType.MinusEqual)
            {
                return false;
            }

            RuntimeValue targetValue = _target.Evaluate(context);
            ResolveMemberTarget(context, targetValue, out Type targetType, out object targetObject, out bool isStatic);

            EventInfo eventInfo = ReflectionMembers.GetEvent(targetType, _memberName.Lexeme, isStatic);

            if (eventInfo == null)
            {
                return false;
            }

            Type eventHandlerType = eventInfo.EventHandlerType;
            context.EnsureTypeAllowed(eventHandlerType, $"access event '{_memberName.Lexeme}'");

            RuntimeValue handlerValue = valueExpression.Evaluate(context);
            Delegate handlerDelegate = (Delegate)RuntimeConversion.ConvertTo(handlerValue.Value, eventHandlerType);

            try
            {
                if (operatorType == TokenType.PlusEqual)
                {
                    eventInfo.AddEventHandler(targetObject, handlerDelegate);
                }
                else
                {
                    eventInfo.RemoveEventHandler(targetObject, handlerDelegate);
                }
            }
            catch (Exception exception)
            {
                throw new ScriptException($"Event '{_memberName.Lexeme}' subscription failed: {exception.Message}");
            }

            PersistValueTypeTarget(context, targetType, targetObject, isStatic);
            result = new RuntimeValue(handlerDelegate, eventHandlerType);
            return true;
        }

        private void PersistValueTypeTarget(ScriptContext context, Type targetType, object targetObject, bool isStatic)
        {
            if (isStatic || !targetType.IsValueType)
            {
                return;
            }

            if (_target is IAssignableExpression assignableTarget)
            {
                assignableTarget.Assign(context, targetObject);
            }
        }

        private static void ResolveMemberTarget(
            ScriptContext context,
            RuntimeValue targetValue,
            out Type targetType,
            out object targetObject,
            out bool isStatic)
        {
            if (targetValue.Value == null)
            {
                throw new ScriptException("Cannot access a member from null.");
            }

            if (targetValue.Value is Type staticType)
            {
                context.EnsureTypeAllowed(staticType, $"access static members of '{staticType.FullName ?? staticType.Name}'");

                targetType = staticType;
                targetObject = null;
                isStatic = true;
                return;
            }

            targetObject = targetValue.Value;
            targetType = targetObject.GetType();
            isStatic = false;

            context.EnsureTypeAllowed(targetType, $"access instance members of '{targetType.FullName ?? targetType.Name}'");
        }
    }
}
