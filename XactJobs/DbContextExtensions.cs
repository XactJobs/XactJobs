using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

using XactJobs.Annotations;

namespace XactJobs
{
    public static class DbContextExtensions
    {
        public static XactJob Enqueue(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob Enqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob Enqueue(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob Enqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob ScheduleAt(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob ScheduleAt<T>(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob ScheduleAt(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob ScheduleAt<T>(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob ScheduleIn(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob ScheduleIn<T>(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob ScheduleIn(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob ScheduleIn<T>(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        private static XactJob AddJob(DbContext dbContext, LambdaExpression lambdaExp, DateTime? scheduledAt, string? queue)
        {
            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var id = dialect.NewJobId();

            var job = XactJobSerializer.FromExpression(lambdaExp, id, scheduledAt, queue);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }
    }
}
