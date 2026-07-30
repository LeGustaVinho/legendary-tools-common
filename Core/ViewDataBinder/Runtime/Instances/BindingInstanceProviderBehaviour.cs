using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public abstract class BindingInstanceProviderBehaviour : MonoBehaviour, IBindingInstanceProvider
    {
        public abstract object GetBindingInstance();

        public abstract Type GetBindingInstanceType();
    }
}
