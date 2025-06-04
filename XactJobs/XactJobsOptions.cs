namespace XactJobs
{
    public class XactJobsOptions
    {
        public int BatchSize { get; set; }

        /// <summary>
        /// Default -1 (means ProcessorCount)
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = -1;

        /// <summary>
        /// Default 120 (jobs runner will prolong the lease every 1/2 of this time)
        /// </summary>
        public int LeaseDurationInSeconds { get; set; } = 120;
    }
}
