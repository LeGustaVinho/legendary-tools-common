namespace LegendaryTools.ViewBinding
{
    public readonly struct BindingSyncResult
    {
        public BindingSyncResult(BindingSyncStatus status, string message)
            : this(status, message, BindingEndpointRole.None)
        {
        }

        public BindingSyncResult(
            BindingSyncStatus status,
            string message,
            BindingEndpointRole endpointRole)
        {
            Status = status;
            Message = message;
            EndpointRole = endpointRole;
        }

        public BindingSyncStatus Status { get; }

        public string Message { get; }

        public BindingEndpointRole EndpointRole { get; }

        public bool IsSuccess => Status == BindingSyncStatus.Success || Status == BindingSyncStatus.NoChange;

        public static BindingSyncResult Success(string message = null)
        {
            return new BindingSyncResult(BindingSyncStatus.Success, message ?? string.Empty);
        }

        public static BindingSyncResult NoChange(string message = null)
        {
            return new BindingSyncResult(BindingSyncStatus.NoChange, message ?? string.Empty);
        }
    }
}
