using System;

namespace LegendaryTools.ViewBinding
{
    public interface IBindingInstanceProvider
    {
        object GetBindingInstance();

        Type GetBindingInstanceType();
    }
}
