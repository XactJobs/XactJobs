// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace XactJobs.Internal
{
    internal class XactJobCompiler
    {
        private static readonly ConcurrentDictionary<XactJobDispatchKey, (Func<IServiceProvider, object?[], Task?>, ParameterInfo[])> _compiledJobs = new();
        
        public static async Task CompileAndRunJobAsync(IServiceScope scope, XactJob job, CancellationToken stoppingToken)
        {
            var jsonArgs = JsonDocument.Parse(job.MethodArgs);

            var args = new object?[jsonArgs.RootElement.GetArrayLength()];

            var key = new XactJobDispatchKey(job.TypeName, job.MethodName, args.Length);

            var (compiledFunc, parameters) = _compiledJobs.GetOrAdd(key, _ =>
            {
                var (type, method) = job.ToMethodInfo(args.Length);
                return (BuildJobDelegate(type, method), method.GetParameters());
            });

            // set the args
            for (var i = 0; i < args.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(CancellationToken))
                {
                    args[i] = stoppingToken;
                }
                else if (jsonArgs.RootElement[i].ValueKind == JsonValueKind.Null)
                {
                    args[i] = null;
                }
                else
                {
                    args[i] = jsonArgs.RootElement[i].Deserialize(parameters[i].ParameterType);
                }
            }

            var resultTask = compiledFunc(scope.ServiceProvider, args);

            if (resultTask != null)
            {
                await resultTask.ConfigureAwait(false);
            }
        }

        public static Func<IServiceProvider, object?[], Task?> BuildJobDelegate(Type type, MethodInfo method)
        {
            var spParam = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
            var argsParam = Expression.Parameter(typeof(object[]), "args");

            var parameters = method.GetParameters();
            var callArgs = new Expression[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var argAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
                var paramType = parameters[i].ParameterType;

                callArgs[i] = Expression.Convert(argAccess, paramType);
            }

            Expression? instanceExpr = null;

            if (!method.IsStatic)
            {
                // Find first constructor
                var ctor = type.GetConstructors().FirstOrDefault() 
                    ?? throw new InvalidOperationException($"Type {type.FullName} does not have a public constructor.");

                var ctorParams = ctor.GetParameters();

                var ctorArgs = ctorParams
                    .Select(p =>
                        (Expression)Expression.Call(
                            typeof(ServiceProviderServiceExtensions),
                            nameof(ServiceProviderServiceExtensions.GetRequiredService),
                            [p.ParameterType],
                            spParam))
                    .ToArray();

                instanceExpr = Expression.New(ctor, ctorArgs);
            }

            var callExpr = Expression.Call(instanceExpr, method, callArgs);

            Expression bodyExpr;

            if (typeof(Task).IsAssignableFrom(method.ReturnType))
            {
                bodyExpr = Expression.Convert(callExpr, typeof(Task));
            }
            else
            {
                var completedTaskProp = typeof(Task).GetProperty(nameof(Task.CompletedTask))!;
                var completedTaskExpr = Expression.Property(null, completedTaskProp);

                // callExpr returns void, so use a Block to sequence call + completedTask
                bodyExpr = Expression.Block(callExpr, completedTaskExpr);
            }

            var lambda = Expression.Lambda<Func<IServiceProvider, object?[], Task?>>(
                bodyExpr, spParam, argsParam);

            return lambda.Compile();
        }
    }
}
