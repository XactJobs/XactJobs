using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace XactJobs
{
    public class XactJob
    {
        private static readonly ConcurrentDictionary<MethodInfo, AsyncStateMachineAttribute?> _asyncStateMachineAttributeCache = new();
        private static readonly ConcurrentDictionary<XactJobDispatchKey, MethodInfo[]> _overloadsCache = new();

        public long Id { get; private set; }

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
        public object?[] Args { get; private set; } = [];

        public string? Queue { get; private set; }

        public XactJob(long id, string typeName, string methodName, object?[] args, string? queue = null)
        {
            Id = id;
            TypeName = typeName;
            MethodName = methodName;
            Args = args;
            Queue = queue;
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

            return new XactJob(0, typeName, methodName, args, queue);
        }

        internal (Type, MethodInfo) ToMethodInfo()
        {
            var type = Type.GetType(TypeName)
                ?? throw new InvalidOperationException($"Type '{TypeName}' could not be loaded.");

            var methods = _overloadsCache.GetOrAdd(GetMethodCacheKey(TypeName, MethodName, Args.Length), _ =>
            [
                .. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == MethodName && m.GetParameters().Length == Args.Length)
            ]);

            var method = methods.FirstOrDefault()
                ?? throw new MissingMethodException($"Method '{MethodName}' with {Args.Length} parameter(s) not found on type '{type.FullName}'.");

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
            var constantExpression = expression as ConstantExpression;

            return constantExpression != null
                ? constantExpression.Value
                : CachedExpressionCompiler.Evaluate(expression);
        }
    }
}
