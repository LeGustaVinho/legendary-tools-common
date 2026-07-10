namespace LegendaryTools.ViewBinding
{
    public enum BindingSyncStatus
    {
        Success = 0,
        NoChange = 1,
        Disabled = 2,
        InvalidSourceCount = 3,
        UnresolvedInstance = 4,
        InvalidMemberPath = 5,
        TypeMismatch = 6,
        ReadFailed = 7,
        WriteFailed = 8,
        FormatterFailed = 9,
        FallbackFailed = 10,
        NullValueRejected = 11,
        ConverterFailed = 12
    }
}
