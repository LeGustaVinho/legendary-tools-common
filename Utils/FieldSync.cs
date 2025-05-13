using UnityEngine;
using System;
using System.Reflection;

namespace LegendaryTools
{
    [ExecuteAlways] // Runs in both editor and play modes.
    public class FieldSync : MonoBehaviour
    {
        #region Public Fields

        [Header("Objects")]
        // Accepts either a Component or a GameObject.
        public UnityEngine.Object Origin;

        public UnityEngine.Object Destination;

        [Header("Selected Members")]
        // Name of the selected member (field or property) via the custom inspector.
        [SerializeField]
        private string originMember;

        [SerializeField] private string destinationMember;

        /// <summary>
        /// Gets or sets the origin member name.
        /// </summary>
        public string OriginMember
        {
            get => originMember;
            set => originMember = value;
        }

        /// <summary>
        /// Gets or sets the destination member name.
        /// </summary>
        public string DestinationMember
        {
            get => destinationMember;
            set => destinationMember = value;
        }

        public enum OperationMode
        {
            OriginToDestination,
            DestinationToOrigin,
            Sync
        }

        [Header("Operation Mode")] public OperationMode mode;

        [Header("Boolean Settings")]
        // Inverts the Boolean value during the operation if both selected members are Boolean.
        public bool Inverse;

        #endregion

        #region Unity Methods

        /// <summary>
        /// Called when a property is changed in the inspector.
        /// </summary>
        private void OnValidate()
        {
            SyncValues();
        }

        /// <summary>
        /// Called every frame after Update. Executed in both play mode and editor (due to [ExecuteAlways]).
        /// </summary>
        private void LateUpdate()
        {
            SyncValues();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Synchronizes the value between Origin and Destination based on the selected operation mode.
        /// Inverts the Boolean value if the Inverse flag is true and both members are Boolean.
        /// </summary>
        private void SyncValues()
        {
            if (Origin != null && Destination != null &&
                !string.IsNullOrEmpty(originMember) && !string.IsNullOrEmpty(destinationMember))
            {
                MemberInfo originInfo = GetMemberInfo(Origin, originMember);
                MemberInfo destinationInfo = GetMemberInfo(Destination, destinationMember);
                if (originInfo == null || destinationInfo == null)
                    return;

                // Check if both members are of Boolean type.
                bool isBooleanSync = (GetMemberType(originInfo) == typeof(bool) &&
                                      GetMemberType(destinationInfo) == typeof(bool));

                switch (mode)
                {
                    case OperationMode.OriginToDestination:
                    {
                        object value = GetMemberValue(Origin, originInfo);
                        if (isBooleanSync && Inverse && value is bool)
                            value = !(bool)value;
                        SetMemberValue(Destination, destinationInfo, value);
                    }
                        break;
                    case OperationMode.DestinationToOrigin:
                    {
                        object value = GetMemberValue(Destination, destinationInfo);
                        if (isBooleanSync && Inverse && value is bool)
                            value = !(bool)value;
                        SetMemberValue(Origin, originInfo, value);
                    }
                        break;
                    case OperationMode.Sync:
                    {
                        // In Sync mode, update Destination using the value from Origin.
                        object value = GetMemberValue(Origin, originInfo);
                        if (isBooleanSync && Inverse && value is bool)
                            value = !(bool)value;
                        SetMemberValue(Destination, destinationInfo, value);
                    }
                        break;
                }
            }
        }

        /// <summary>
        /// Retrieves the member (field or property) from the object by name, traversing its class hierarchy.
        /// </summary>
        /// <param name="obj">The target object (Component or GameObject).</param>
        /// <param name="memberName">The name of the member.</param>
        /// <returns>The MemberInfo if found; otherwise, null.</returns>
        private MemberInfo GetMemberInfo(UnityEngine.Object obj, string memberName)
        {
            Type type = obj.GetType();
            while (type != null)
            {
                // Search for a field declared in this class.
                FieldInfo field = type.GetField(memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
                // Search for a property declared in this class.
                PropertyInfo prop = type.GetProperty(memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (prop != null)
                    return prop;
                type = type.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Gets the value of the member (field or property) from the target using reflection.
        /// </summary>
        /// <param name="obj">The target object (Component or GameObject).</param>
        /// <param name="member">The member information.</param>
        /// <returns>The value of the member.</returns>
        private object GetMemberValue(UnityEngine.Object obj, MemberInfo member)
        {
            if (member is FieldInfo field)
                return field.GetValue(obj);
            if (member is PropertyInfo prop)
                return prop.GetValue(obj, null);
            return null;
        }

        /// <summary>
        /// Sets the value of the member (field or property) on the target using reflection.
        /// Uses a try/catch block to avoid errors if the member is read-only.
        /// </summary>
        /// <param name="obj">The target object (Component or GameObject).</param>
        /// <param name="member">The member information.</param>
        /// <param name="value">The value to set.</param>
        private void SetMemberValue(UnityEngine.Object obj, MemberInfo member, object value)
        {
            try
            {
                if (member is FieldInfo field)
                    field.SetValue(obj, value);
                else if (member is PropertyInfo prop)
                {
                    // Check if the property can be written to.
                    if (prop.CanWrite)
                        prop.SetValue(obj, value, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not set value on {obj.GetType().Name}.{member.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the type of the specified member (field or property).
        /// </summary>
        /// <param name="member">The member information.</param>
        /// <returns>The type of the member.</returns>
        private Type GetMemberType(MemberInfo member)
        {
            if (member is FieldInfo field)
                return field.FieldType;
            else if (member is PropertyInfo prop)
                return prop.PropertyType;
            return null;
        }

        #endregion
    }
}