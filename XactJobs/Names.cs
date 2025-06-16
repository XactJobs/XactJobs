namespace XactJobs
{
    internal class Names
    {
        public const string QueueDefault = "*";

        public const string XactJobSchema = "xact_jobs";
        public const string XactJobTable = "job";
        public const string XactJobArchiveTable = "job_archive";
        public const string XactJobPeriodicTable = "job_periodic";

        public const string ColId = "id";
        public const string ColCreatedAt = "created_at";
        public const string ColScheduledAt = "scheduled_at";
        public const string ColCompletedAt = "completed_at";
        public const string ColLeasedUntil = "leased_until";
        public const string ColLeaser = "leaser";
        public const string ColTypeName = "type_name";
        public const string ColMethodName = "method_name";
        public const string ColMethodArgs = "method_args";
        public const string ColStatus = "status";
        public const string ColQueue = "queue";
        public const string ColPeriodicJobId = "periodic_job_id";

        public const string ColErrorCount = "error_count";
        public const string ColErrorTime = "error_time";
        public const string ColErrorMessage = "error_message";
        public const string ColErrorStackTrace = "error_stack_trace";

        public const string ColCronExpression = "cron_expression";
        public const string ColName = "name";
        public const string ColUpdatedAt = "updated_at";
        public const string ColIsActive = "is_active";

    }
}
