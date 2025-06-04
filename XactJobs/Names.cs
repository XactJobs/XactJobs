namespace XactJobs
{
    internal class Names
    {
        public const string XactJobSchema = "xact_jobs";
        public const string XactJobTable = "job";

        public const string ColId = "id";
        public const string ColCreatedAt = "created_at";
        public const string ColUpdatedAt = "updated_at";
        public const string ColScheduledAt = "created_at";
        public const string ColLeasedUntil = "leased_until";
        public const string ColLeaser = "leaser";
        public const string ColTypeName = "type_name";
        public const string ColMethodName = "method_name";
        public const string ColMethodArgs = "method_args";
        public const string ColStatus = "status";
        public const string ColQueue = "queue";

        public const string ColErrorCount = "error_count";
        public const string ColErrorMessage = "error_message";
        public const string ColErrorStackTrace = "error_stack_trace";
    }
}
