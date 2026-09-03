using System;
using System.Reflection;

namespace FlatData
{
    internal abstract class MemberAccessor
    {
        protected MemberAccessor(string name, Type memberType)
        {
            Name = name;
            MemberType = memberType;
        }

        public string Name { get; }

        public Type MemberType { get; }

        public abstract bool CanRead { get; }

        public abstract bool CanWrite { get; }

        public abstract object GetValue(object instance);

        public abstract void SetValue(object instance, object value);
    }

    internal sealed class FieldAccessor : MemberAccessor
    {
        private readonly FieldInfo _field;

        public FieldAccessor(FieldInfo field)
            : base(field.Name, field.FieldType)
        {
            _field = field;
        }

        public override bool CanRead => true;

        public override bool CanWrite => !_field.IsInitOnly && !_field.IsLiteral;

        public override object GetValue(object instance)
        {
            return _field.GetValue(instance);
        }

        public override void SetValue(object instance, object value)
        {
            _field.SetValue(instance, value);
        }
    }

    internal sealed class PropertyAccessor : MemberAccessor
    {
        private readonly PropertyInfo _property;

        public PropertyAccessor(PropertyInfo property)
            : base(property.Name, property.PropertyType)
        {
            _property = property;
        }

        public override bool CanRead => _property.GetGetMethod(true) != null;

        public override bool CanWrite => _property.GetSetMethod(true) != null;

        public override object GetValue(object instance)
        {
            return _property.GetValue(instance, null);
        }

        public override void SetValue(object instance, object value)
        {
            _property.SetValue(instance, value, null);
        }
    }
}
