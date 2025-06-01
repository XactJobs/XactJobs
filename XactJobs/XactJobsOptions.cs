namespace XactJobs
{
    public class XactJobsOptions
    {
        public required ISqlDialect Dialect { get; set; }
        public int BatchSize { get; set; }
        public int MaxDegreeOfParallelism { get; set; }
    }
}
