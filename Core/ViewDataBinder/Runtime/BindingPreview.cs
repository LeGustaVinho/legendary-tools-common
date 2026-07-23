namespace LegendaryTools.ViewBinding
{
    public readonly struct BindingPreview
    {
        public BindingPreview(
            object sourceValue,
            object convertedSourceValue,
            object targetValue,
            object convertedTargetValue,
            BindingSyncResult result)
        {
            SourceValue = sourceValue;
            ConvertedSourceValue = convertedSourceValue;
            TargetValue = targetValue;
            ConvertedTargetValue = convertedTargetValue;
            Result = result;
        }

        public object SourceValue { get; }

        public object ConvertedSourceValue { get; }

        public object TargetValue { get; }

        public object ConvertedTargetValue { get; }

        public BindingSyncResult Result { get; }
    }
}
