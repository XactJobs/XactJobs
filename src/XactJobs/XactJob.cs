namespace XactJobs
{
    public class XactJob: XactJobBase
    {
        public DateTime? LeasedUntil { get; init; }
        public Guid? Leaser { get; init; }

        // needed for the FK
        public XactJobPeriodic? PeriodicJob { get; init; }

        public XactJob(long id,
                       DateTime scheduledAt,
                       string typeName,
                       string methodName,
                       string methodArgs,
                       string queue,
                       string? periodicJobId = null,
                       string? cronExpression = null,
                       int errorCount = 0,
                       Guid? leaser = null,
                       DateTime? leasedUntil = null)
            : base(id,
                   scheduledAt,
                   typeName,
                   methodName,
                   methodArgs,
                   queue,
                   periodicJobId,
                   cronExpression,
                   errorCount)
        {
            Leaser = leaser;
            LeasedUntil = leasedUntil;
        }
    }
} 