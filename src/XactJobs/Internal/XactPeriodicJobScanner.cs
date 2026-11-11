// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using XactJobs.CronUtil;

namespace XactJobs.Internal
{
    /// <summary>
    /// The periodic jobs of the methods with <see cref="XactPeriodicJobAttribute"/>: each one as the expression of a call of the method,
    /// as <c>WithPeriodicJob</c> gets it, so that the serializer stores the same strings for both.
    /// </summary>
    internal static class XactPeriodicJobScanner
    {
        internal sealed record Definition(string Id, string CronExpression, string? Queue, LambdaExpression JobExpression);

        /// <summary>
        /// Adds the periodic jobs of the options' assemblies to the options: the default queue's, or their isolated queue's. Called once
        /// the options are complete, so that the queues and the other periodic jobs are known.
        /// </summary>
        /// <exception cref="InvalidOperationException">With all the problems found, e.g. an invalid method, or an id used twice.</exception>
        public static void AddTo<TDbContext>(XactJobsOptions<TDbContext> options) where TDbContext : DbContext
        {
            if (options.PeriodicJobAssemblies.Count == 0) return;

            var errors = new List<string>();

            var definitions = Find(options.PeriodicJobAssemblies.Distinct().SelectMany(a => a.GetTypes()), errors);

            var definedIds = options.PeriodicJobs.Keys
                .Concat(options.IsolatedQueues.Values.SelectMany(q => q.PeriodicJobs.Keys))
                .ToHashSet();

            foreach (var definition in definitions)
            {
                XactJobsOptionsBase<TDbContext> queueOptions = options;

                if (definition.Queue != null && definition.Queue != QueueNames.Default)
                {
                    if (!options.IsolatedQueues.TryGetValue(definition.Queue, out var isolatedQueue))
                    {
                        errors.Add($"periodic job '{definition.Id}': the queue '{definition.Queue}' isn't configured");
                        continue;
                    }

                    queueOptions = isolatedQueue;
                }

                if (definedIds.Contains(definition.Id))
                {
                    errors.Add($"periodic job '{definition.Id}': the id is also defined with WithPeriodicJob");
                    continue;
                }

                queueOptions.PeriodicJobs[definition.Id] = (definition.JobExpression, definition.CronExpression);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"Invalid [XactPeriodicJob] attributes:\n- {string.Join("\n- ", errors)}");
            }
        }

        /// <summary>
        /// The periodic jobs of the types' methods. A problem with one of them is added to the errors, and leaves it out.
        /// </summary>
        public static List<Definition> Find(IEnumerable<Type> types, List<string> errors)
        {
            var definitions = new List<Definition>();
            var ids = new Dictionary<string, MethodInfo>();

            const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(AllDeclared))
                {
                    foreach (var attribute in method.GetCustomAttributes<XactPeriodicJobAttribute>(inherit: false))
                    {
                        var where = $"{type.FullName}.{method.Name}";

                        if (string.IsNullOrWhiteSpace(attribute.Id))
                        {
                            errors.Add($"{where}: the id is empty");
                            continue;
                        }

                        var error = Validate(type, method, attribute);

                        if (error == null && ids.TryGetValue(attribute.Id, out var other))
                        {
                            error = $"the id is also on {other.DeclaringType?.FullName}.{other.Name}";
                        }

                        if (error != null)
                        {
                            errors.Add($"periodic job '{attribute.Id}' ({where}): {error}");
                            continue;
                        }

                        ids[attribute.Id] = method;

                        definitions.Add(new Definition(attribute.Id, attribute.CronExpression, attribute.Queue, BuildExpression(type, method, attribute.Config)));
                    }
                }
            }

            return definitions;
        }

        private static string? Validate(Type type, MethodInfo method, XactPeriodicJobAttribute attribute)
        {
            if (!method.IsPublic) return "the method isn't public";

            if (method.IsGenericMethodDefinition) return "the method is generic";

            if (type.ContainsGenericParameters) return "its type is generic";

            if (!method.IsStatic)
            {
                if (type.IsAbstract || type.IsInterface) return "an instance method of an abstract type: the worker can't create it";

                if (type.GetConstructors().Length == 0) return "its type has no public constructor: the worker can't create it";
            }

            if (method.ReturnType != typeof(void) && !typeof(Task).IsAssignableFrom(method.ReturnType))
            {
                return $"it returns {method.ReturnType.Name}: only void or a Task";
            }

            if (method.ReturnType == typeof(void) && method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), inherit: false))
            {
                return "it's async void: the worker can't wait for it, return a Task";
            }

            var strings = 0;

            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(string))
                {
                    strings++;
                }
                else if (parameter.ParameterType != typeof(CancellationToken) && parameter.ParameterType != typeof(XactJobContext))
                {
                    return $"the parameter {parameter.Name} is a {parameter.ParameterType.Name}: only a CancellationToken, an XactJobContext and one string (the config)";
                }
            }

            if (strings > 1) return "it has more than one string parameter: only one, for the config";

            if (attribute.Config != null && strings == 0) return "it has a config, but no string parameter for it";

            try
            {
                // also finds expressions whose next time can't be found
                new CronSequenceGenerator(attribute.CronExpression, TimeZoneInfo.Utc).NextUtc(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                return $"invalid cron expression '{attribute.CronExpression}': {ex.Message}";
            }

            return null;
        }

        /// <summary>
        /// A call as a job expression gives it: <c>x => x.Run("config", CancellationToken.None, null)</c> or a static call. The
        /// CancellationToken and the XactJobContext are stored as null, and the worker passes its own.
        /// </summary>
        private static LambdaExpression BuildExpression(Type type, MethodInfo method, string? config)
        {
            var target = method.IsStatic ? null : Expression.Parameter(type, "x");

            var arguments = method.GetParameters()
                .Select(p => p.ParameterType == typeof(string)
                    ? Expression.Constant(config, typeof(string))
                    : (Expression)Expression.Default(p.ParameterType))
                .ToArray();

            var call = Expression.Call(target, method, arguments);

            return target == null ? Expression.Lambda(call) : Expression.Lambda(call, target);
        }
    }
}
