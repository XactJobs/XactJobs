using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

using XactJobs.Annotations;

namespace XactJobs
{
    public static class DbContextExtensions
    {
        public static XactJob Enqueue(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression)
        {
            return AddJob(dbContext, jobExpression);
        }

        public static XactJob Enqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression)
        {
            return AddJob(dbContext, jobExpression);
        }

        public static XactJob Enqueue(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression)
        {
            return AddJob(dbContext, jobExpression);
        }

        public static XactJob Enqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression)
        {
            return AddJob(dbContext, jobExpression);
        }

        private static XactJob AddJob(DbContext dbContext, LambdaExpression lambdaExp, string? queue = null)
        {
            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var id = dialect.NewJobId();

            var job = XactJobSerializer.FromExpression(lambdaExp, id, queue);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }
    }
}
