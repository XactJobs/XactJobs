using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace XactJobs
{
    public class XactJob
    {
        private static readonly ConcurrentDictionary<MethodInfo, AsyncStateMachineAttribute?> _asyncStateMachineAttributeCache = new();
        private static readonly ConcurrentDictionary<XactJobDispatchKey, MethodInfo[]> _overloadsCache = new();

        public long Id { get; private set; }

        /// <summary>
        /// Job status
        /// </summary>
        public XactJobStatus Status { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime ScheduledAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        /// <summary>
        /// Assembly qualified name of the declaring type
        /// </summary>
        public string TypeName { get; private set; }

        /// <summary>
        /// Method name to be called
        /// </summary>
        public string MethodName { get; private set; }

        /// <summary>
        /// Arguments to be passed to the method
        /// </summary>
        public string MethodArgs { get; private set; }

        public string? Queue { get; private set; }

        public int ErrorCount { get; private set; }

        public string? ErrorMessage { get; private set; }

        public string? ErrorStackTrace { get; private set; }

        public XactJob(long id,
                       DateTime createdAt,
                       DateTime scheduledAt,
                       string typeName,
                       string methodName,
                       string methodArgs,
                       string? queue = null,
                       DateTime? updatedAt = null,
                       int errorCount = 0,
                       string? lastErrorMessage = null,
                       string? lastErrorStackTrace = null)
        {
            Id = id;
            TypeName = typeName;
            MethodName = methodName;
            MethodArgs = methodArgs;
            Queue = queue;
            CreatedAt = createdAt;
            ScheduledAt = scheduledAt;
            UpdatedAt = updatedAt;
            ErrorCount = errorCount;
            ErrorMessage = lastErrorMessage;
            ErrorStackTrace = lastErrorStackTrace;
        }

        internal void MarkCompleted()
        {
            UpdatedAt = DateTime.Now;
            Status = XactJobStatus.Completed;
        }

        internal void MarkFailed(Exception ex)
        {
            UpdatedAt = DateTime.UtcNow;
            ErrorCount = ErrorCount + 1;
            ErrorMessage = ex.Message;
            ErrorStackTrace = ex.StackTrace;

            // TODO Add retry strategy

            Status = ErrorCount <= 10 ? XactJobStatus.Failed : XactJobStatus.Cancelled;
            
            if (Status == XactJobStatus.Failed)
            {
                ScheduledAt = DateTime.UtcNow.AddSeconds(10);
            }
        }

        internal static XactJob FromExpression(LambdaExpression lambdaExp, string? queue)
        {
            var callExpression = lambdaExp.Body as MethodCallExpression 
                ?? throw new ArgumentException("Expression body should be a simple method call.", nameof(lambdaExp));

            Validate(callExpression);

            var type = callExpression.Object?.Type ?? callExpression.Method.DeclaringType
                ?? throw new NotSupportedException("XactJobs does not support global methods.");

            var typeName = GetSimpleTypeName(type);

            var methodName = callExpression.Method.Name;

            var args = GetExpressionValues(callExpression.Arguments);

            var serializedArgs = JsonSerializer.Serialize(args);

            var scheduledAt = DateTime.UtcNow;

            return new XactJob(0, scheduledAt, scheduledAt, typeName, methodName, serializedArgs, queue);
        }

        internal (Type, MethodInfo) ToMethodInfo()
        {
            var type = Type.GetType(TypeName)
                ?? throw new InvalidOperationException($"Type '{TypeName}' could not be loaded.");

            var methods = _overloadsCache.GetOrAdd(GetMethodCacheKey(TypeName, MethodName, MethodArgs.Length), _ =>
            [
                .. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == MethodName && m.GetParameters().Length == MethodArgs.Length)
            ]);

            var method = methods.FirstOrDefault()
                ?? throw new MissingMethodException($"Method '{MethodName}' with {MethodArgs.Length} parameter(s) not found on type '{type.FullName}'.");

            return (type, method);
        }

        private static string GetSimpleTypeName(Type type)
        {
            return string.Join(", ", type.FullName, type.Assembly.GetName().Name);
        }

        private static XactJobDispatchKey GetMethodCacheKey(Type type, MethodInfo method)
        {
            return GetMethodCacheKey(GetSimpleTypeName(type), method.Name, method.GetParameters().Length);
        }

        private static XactJobDispatchKey GetMethodCacheKey(string typeName, string methodName, int paramCount)
        {
            return new XactJobDispatchKey(typeName, methodName, paramCount);
        }

        private static void Validate(MethodCallExpression callExpression)
        {
            var method = callExpression.Method;

            if (!method.IsPublic)
            {
                throw new NotSupportedException("XactJobs support only public methods.");
            }

            if (method.ContainsGenericParameters)
            {
                throw new NotSupportedException("XactJobs method can not be generic.");
            }

            if (method.ReturnType == typeof(void) &&
                _asyncStateMachineAttributeCache.GetOrAdd(method, static m => m.GetCustomAttribute<AsyncStateMachineAttribute>()) != null)
            {
                throw new NotSupportedException("XactJobs does not support async void methods. Use async Task instead.");
            }

            var parameters = method.GetParameters();

            // Check for overloads with same name and same param count
            var type = callExpression.Object?.Type ?? method.DeclaringType
                ?? throw new NotSupportedException("XactJobs does not support global methods.");

            var overloads = _overloadsCache.GetOrAdd(GetMethodCacheKey(type, method), _ =>
            [
                .. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == method.Name && m.GetParameters().Length == parameters.Length)
            ]);

            if (overloads.Length > 1)
            {
                throw new NotSupportedException(
                    $"Type '{type.FullName}' contains multiple public methods named '{method.Name}' with {parameters.Length} parameter(s). " +
                    "This is not supported: job deserialization would be ambiguous.");
            }

            if (parameters.Length != callExpression.Arguments.Count)
            {
                throw new ArgumentException("Argument count must be equal to method parameter count.");
            }

            foreach (var parameter in parameters)
            {
                if (parameter.IsOut)
                {
                    throw new NotSupportedException("XactJobs does not support output parameters.");
                }

                if (parameter.ParameterType.IsByRef)
                {
                    throw new NotSupportedException("XactJobs does not support parameters passed by reference.");
                }

                var parameterTypeInfo = parameter.ParameterType.GetTypeInfo();
                
                if (parameterTypeInfo.IsSubclassOf(typeof(Delegate)) || parameterTypeInfo.IsSubclassOf(typeof(Expression)))
                {
                    throw new NotSupportedException("XactJobs does not support anonymous functions, delegates and lambda expressions in job method parameters.");
                }
            }
        }

        private static object?[] GetExpressionValues(IReadOnlyCollection<Expression> expressions)
        {
            var result = expressions.Count > 0 ? new object?[expressions.Count] : [];
            var index = 0;

            foreach (var expression in expressions)
            {
                result[index++] = GetExpressionValue(expression);
            }

            return result;
        }

        private static object? GetExpressionValue(Expression expression)
        {
            return expression is ConstantExpression constantExpression
                ? constantExpression.Value
                : CachedExpressionCompiler.Evaluate(expression);
        }
    }
}
