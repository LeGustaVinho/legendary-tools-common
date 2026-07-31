using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [CreateAssetMenu(
        fileName = "ObjectNullBindingConverter",
        menuName = "Legendary Tools/View Data Binder/Converters/Object Null to Boolean")]
    public sealed class ObjectNullBindingConverter : BindingConverter
    {
        [SerializeField] private bool trueWhenNotNull = true;

        public bool TrueWhenNotNull
        {
            get => trueWhenNotNull;
            set => trueWhenNotNull = value;
        }

        public override Type SourceType => typeof(object);

        public override Type TargetType => typeof(bool);

        public override bool CanConvert(Type sourceType, Type targetType)
        {
            return sourceType != null && targetType == typeof(bool);
        }

        public override bool TryConvert(object sourceValue, out object targetValue, out string error)
        {
            bool isNotNull = sourceValue != null;
            if (sourceValue is UnityEngine.Object unityObject)
            {
                isNotNull = unityObject != null;
            }

            targetValue = trueWhenNotNull ? isNotNull : !isNotNull;
            error = string.Empty;
            return true;
        }
    }
}
