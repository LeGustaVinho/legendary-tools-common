using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class ViewDataEventBinding
    {
        [SerializeField] private string id;
        [SerializeField] private string name = "Event Binding";
        [SerializeField] private bool enabled = true;
        [SerializeField] private BindingUpdateTiming updateTiming = BindingUpdateTiming.Update;
        [SerializeField] private bool triggerOnInitialize;
        [SerializeField] private BindingErrorPolicy errorPolicy = BindingErrorPolicy.ReportOnly;
        [SerializeField] private List<BindingSource> sources =
            new List<BindingSource> { new BindingSource() };
        [SerializeField] private List<EventBindingCondition> conditions =
            new List<EventBindingCondition> { new EventBindingCondition() };

        public string Id => id;

        public string Name => name;

        public bool Enabled => enabled;

        public BindingUpdateTiming UpdateTiming => updateTiming;

        public bool TriggerOnInitialize => triggerOnInitialize;

        public BindingErrorPolicy ErrorPolicy => errorPolicy;

        public IReadOnlyList<BindingSource> Sources => sources;

        public IReadOnlyList<EventBindingCondition> Conditions => conditions;

        internal void EnsureId()
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }
        }
    }
}
