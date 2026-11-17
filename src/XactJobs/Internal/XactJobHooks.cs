// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace XactJobs.Internal
{
    /// <summary>The app's job filters and failure handlers, shared by the runners of a dispatcher.</summary>
    internal sealed class XactJobHooks(IReadOnlyList<Type> filters, IReadOnlyList<Type> failureHandlers)
    {
        public IReadOnlyList<Type> Filters { get; } = filters;

        public IReadOnlyList<Type> FailureHandlers { get; } = failureHandlers;

        /// <summary>
        /// Runs the job, inside the filters if there are any: each one resolved from the job's scope, the first one outermost. Without
        /// filters, it's the job alone, with nothing allocated for them.
        /// </summary>
        public Task RunJobAsync(XactJobCall call, XactJob job, IServiceProvider services, DbContext dbContext, XactJobContext jobContext, CancellationToken stoppingToken)
        {
            if (Filters.Count == 0)
            {
                return call.InvokeAsync(services);
            }

            var context = new XactJobExecutingContext(job, call.Method, call.Arguments, services, dbContext, jobContext, stoppingToken);

            return new FilterChain(Filters, call, services, context).RunAsync();
        }

        /// <summary>
        /// The filters of one job's run: <see cref="NextAsync"/> is the <c>next</c> each filter gets, which runs the following filter, and
        /// after the last one the job. One allocation for the whole chain.
        /// </summary>
        private sealed class FilterChain
        {
            private readonly IReadOnlyList<Type> _filters;
            private readonly XactJobCall _call;
            private readonly IServiceProvider _services;
            private readonly XactJobExecutingContext _context;
            private readonly Func<Task> _next;

            private int _nextFilter;
            private bool _isJobInvoked;
            private ExceptionDispatchInfo? _jobFailure;

            public FilterChain(IReadOnlyList<Type> filters, XactJobCall call, IServiceProvider services, XactJobExecutingContext context)
            {
                _filters = filters;
                _call = call;
                _services = services;
                _context = context;
                _next = NextAsync;
            }

            public async Task RunAsync()
            {
                await NextAsync().ConfigureAwait(false);

                // a filter that swallowed the job's failure: it's still a failure, because the job's changes (saved or not, in its own
                // transaction too) can't be told apart from a completed job's, and the worker would save and commit them
                _jobFailure?.Throw();
            }

            private Task NextAsync()
            {
                if (_nextFilter < _filters.Count)
                {
                    var filter = (IXactJobFilter)_services.GetRequiredService(_filters[_nextFilter++]);

                    return filter.InvokeAsync(_context, _next);
                }

                if (_isJobInvoked)
                {
                    throw new InvalidOperationException("A job filter called next() more than once: a job runs once per attempt.");
                }

                _isJobInvoked = true;

                return InvokeJobAsync();
            }

            private async Task InvokeJobAsync()
            {
                try
                {
                    await _call.InvokeAsync(_services).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _jobFailure = ExceptionDispatchInfo.Capture(ex);
                    throw;
                }
            }
        }
    }
}
