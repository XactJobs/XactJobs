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

using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace XactJobs.Internal
{
    /// <summary>
    /// Turns a job's lambda expression into the three strings it's stored as, and those strings back into the method to run:
    /// <list type="bullet">
    /// <item><c>TypeName</c>: the job's type, <c>Namespace.Type, AssemblySimpleName</c> (generic arguments in double brackets, without
    /// versions or public key tokens)</item>
    /// <item><c>MethodName</c>: the method with its parameter types, <c>Send(System.String, System.Int32)</c></item>
    /// <item><c>MethodArgs</c>: the arguments, evaluated when the job is enqueued, as a JSON array, exactly as
    /// <c>JsonSerializer.Serialize(object?[])</c> writes it with the default options</item>
    /// </list>
    /// Databases hold these strings, and a periodic job is compared with its definition by them, so they must never change
    /// (see SerializerFormatTests).
    /// </summary>
    internal static class XactJobSerializer
    {
        private const BindingFlags PublicMethods = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        /// <summary>A writer's buffer larger than this isn't kept for the next job, so one large job doesn't hold its memory.</summary>
        private const int MaxCachedBufferSize = 64 * 1024;

        /// <summary>What doesn't depend on the arguments, per method and job type: the strings, and the validation done once.</summary>
        private static readonly ConcurrentDictionary<JobMethodKey, JobMethod> _jobMethods = new();

        /// <summary>The run side: the types by their stored names.</summary>
        private static readonly ConcurrentDictionary<string, Type> _types = new();

        /// <summary>The methods, constructors and conversion operators of the arguments that reflection can call (<see cref="CanInvoke"/>).</summary>
        private static readonly ConcurrentDictionary<MethodBase, bool> _invocableMethods = new();

        /// <summary>A JSON writer and its buffer per thread, reused by the jobs enqueued on it.</summary>
        [ThreadStatic]
        private static ArgumentsWriter? _cachedWriter;

        internal static XactJob FromExpression(LambdaExpression lambdaExp,
                                               DateTime? scheduleAtUtc,
                                               string? queue,
                                               Guid? batchId = null,
                                               Guid? awaitsBatchId = null,
                                               XactJobContinuation? continuationCondition = null)
        {
            if (scheduleAtUtc.HasValue && scheduleAtUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException($"The scheduled time must be in UTC (its Kind is {scheduleAtUtc.Value.Kind}).", nameof(scheduleAtUtc));
            }

            var (typeName, methodName, methodArgs) = Serialize(lambdaExp);

            return new XactJob(0, scheduleAtUtc ?? DateTime.UtcNow, typeName, methodName, methodArgs, queue ?? QueueNames.Default)
            {
                BatchId = batchId,
                AwaitsBatchId = awaitsBatchId,
                ContinuationCondition = continuationCondition,
            };
        }

        internal static XactJobPeriodic FromExpressionPeriodic(LambdaExpression lambdaExp, string id, string cronExp, string? queue)
        {
            var (typeName, methodName, methodArgs) = Serialize(lambdaExp);

            var now = DateTime.UtcNow;

            return new XactJobPeriodic(id,
                                       now,
                                       now,
                                       cronExp,
                                       typeName,
                                       methodName,
                                       methodArgs,
                                       queue ?? QueueNames.Default,
                                       isActive: true,
                                       XactJobPeriodic.NewVersion());
        }

        /// <summary>
        /// The type to create (for an instance method) and the method to call, from the job's stored strings. The compiler calls it
        /// once per job method, and caches the result.
        /// <para>
        /// The method is the public one (instance or static, inherited too) whose signature is the stored <c>MethodName</c>. If there
        /// isn't one (a job stored before 0.1.7, without a signature, or a parameter type that changed since), it's the only method
        /// with that name and <paramref name="paramCount"/> parameters. When a method hides another one with <c>new</c>, the one
        /// declared on the most derived type.
        /// </para>
        /// </summary>
        internal static (Type, MethodInfo) ToMethodInfo(this XactJob job, int paramCount)
        {
            // Type.GetType throws if it's not found (the job fails, with the message in its history): failures aren't cached
            var type = _types.GetOrAdd(job.TypeName, static typeName => Type.GetType(typeName, throwOnError: true)!);

            return (type, FindMethod(type, job.MethodName, paramCount));
        }

        /// <summary>The stored <c>MethodName</c> of a method: <c>Name(System.String, System.Nullable`1[System.Int32])</c>.</summary>
        internal static string GetMethodSignature(MethodInfo method)
        {
            var parameters = method.GetParameters();

            if (parameters.Length == 0) return method.Name + "()";

            var sb = new StringBuilder(method.Name, 64).Append('(');

            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");

                AppendParameterTypeName(sb, parameters[i].ParameterType);
            }

            return sb.Append(')').ToString();
        }

        private static (string TypeName, string MethodName, string MethodArgs) Serialize(LambdaExpression lambdaExp)
        {
            ArgumentNullException.ThrowIfNull(lambdaExp);

            var call = GetMethodCall(lambdaExp);

            // an instance method's job type is the static type of its target: for x => x.Run() on a DerivedJobs, DerivedJobs, also when
            // Run is declared on a base class. The target isn't evaluated: the worker creates its own instance.
            var jobType = call.Object?.Type
                ?? call.Method.DeclaringType
                ?? throw new NotSupportedException($"The job method {call.Method.Name} has no declaring type.");

            var jobMethod = _jobMethods.GetOrAdd(new JobMethodKey(call.Method, jobType), static key => JobMethod.Create(key.Method, key.JobType));

            return (jobMethod.TypeName, jobMethod.MethodName, SerializeArguments(call.Arguments, jobMethod.StoredAsNull));
        }

        private static MethodCallExpression GetMethodCall(LambdaExpression lambdaExp)
        {
            var body = lambdaExp.Body;

            // e.g. a Task<int> returned as a Task
            if (body is UnaryExpression { NodeType: ExpressionType.Convert, Operand: MethodCallExpression converted })
            {
                body = converted;
            }

            return body as MethodCallExpression
                ?? throw new ArgumentException($"A job must be a method call, e.g. () => MyJobs.Send(id) or x => x.RunAsync(id), not {body}.", nameof(lambdaExp));
        }

        #region Arguments

        private static string SerializeArguments(ReadOnlyCollection<Expression> arguments, bool[] storedAsNull)
        {
            if (arguments.Count == 0) return "[]";

            // all the arguments are evaluated before any of them is serialized, as the call would evaluate them
            var values = new object?[arguments.Count];

            for (var i = 0; i < values.Length; i++)
            {
                // a CancellationToken or an XactJobContext is stored as null, and not evaluated: the worker passes its own
                if (!storedAsNull[i])
                {
                    values[i] = Evaluate(arguments[i]);
                }
            }

            return WriteJson(values);
        }

        /// <summary>
        /// Writes the values as <c>JsonSerializer.Serialize(object?[])</c> does: each one by its runtime type, with the default options
        /// (the writer's default encoder is the serializer's, which escapes HTML characters and non-ASCII ones).
        /// </summary>
        private static string WriteJson(object?[] values)
        {
            // taken from the thread while it's used: serializing a value runs its getters, which could enqueue a job too
            var writer = _cachedWriter ?? new ArgumentsWriter();

            _cachedWriter = null;

            var json = writer.Start();

            json.WriteStartArray();

            foreach (var value in values)
            {
                WriteValue(json, value);
            }

            json.WriteEndArray();
            json.Flush();

            var result = Encoding.UTF8.GetString(writer.Buffer.WrittenSpan);

            // not kept after an exception: the next job would get a fresh one anyway
            if (writer.Buffer.Capacity <= MaxCachedBufferSize)
            {
                _cachedWriter = writer;
            }

            return result;
        }

        private static void WriteValue(Utf8JsonWriter json, object? value)
        {
            if (value == null)
            {
                json.WriteNullValue();
                return;
            }

            // the most common types written directly, as their converters write them; the others by the serializer
            var type = value.GetType();

            if (type == typeof(string)) json.WriteStringValue((string)value);
            else if (type == typeof(int)) json.WriteNumberValue((int)value);
            else if (type == typeof(long)) json.WriteNumberValue((long)value);
            else if (type == typeof(bool)) json.WriteBooleanValue((bool)value);
            else if (type == typeof(Guid)) json.WriteStringValue((Guid)value);
            else JsonSerializer.Serialize(json, value, type);
        }

        /// <summary>
        /// Evaluates an argument's expression. The common ones are evaluated here, without compiling them: constants, captured variables,
        /// fields and properties, conversions that don't change the value, and the creation of objects, arrays and collections, and
        /// method calls, from arguments evaluated the same way. Whether a node can be evaluated here depends only on its shape, never
        /// on the values, so a subexpression is never evaluated twice. The others are compiled, cached by their shape
        /// (<see cref="Microsoft.Web.Mvc.CachedExpressionCompiler"/>).
        /// </summary>
        private static object? Evaluate(Expression expression)
        {
            switch (expression)
            {
                case ConstantExpression constant:
                    return constant.Value;

                // a member of a Nullable (HasValue, Value) works on a null instance: compiled
                case MemberExpression member when member.Expression == null || !IsNullable(member.Expression.Type):
                    return EvaluateMember(member);

                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert:
                    if (convert.Method != null && !convert.IsLifted && CanInvoke(convert.Method))
                    {
                        // a user-defined conversion operator, e.g. int to decimal
                        return Invoke(convert.Method, null, [Evaluate(convert.Operand)]);
                    }

                    if (convert.Method == null && IsUnchangedByConversion(convert.Operand.Type, convert.Type))
                    {
                        // a boxing or reference conversion, e.g. a value passed as an object: our values are already objects
                        return Evaluate(convert.Operand);
                    }

                    break;

                case NewExpression create:
                    return EvaluateNew(create);

                case MemberInitExpression init when init.Bindings.All(b => b is MemberAssignment):
                    return EvaluateMemberInit(init);

                case ListInitExpression list when list.Initializers.All(i => !i.AddMethod.IsStatic && CanInvoke(i.AddMethod)):
                    return EvaluateListInit(list);

                case NewArrayExpression { NodeType: ExpressionType.NewArrayInit } array:
                    return EvaluateNewArray(array);

                case MethodCallExpression call when CanInvoke(call.Method) && (call.Object == null || !IsNullable(call.Object.Type)):
                    return Invoke(call.Method, EvaluateInstance(call.Object, call.Method.IsStatic), EvaluateAll(call.Arguments));

                case ConditionalExpression conditional:
                    return (bool)Evaluate(conditional.Test)! ? Evaluate(conditional.IfTrue) : Evaluate(conditional.IfFalse);

                case DefaultExpression @default:
                    return GetDefault(@default.Type);
            }

            return Microsoft.Web.Mvc.CachedExpressionCompiler.Evaluate(expression);
        }

        private static object? EvaluateMember(MemberExpression member)
        {
            switch (member.Member)
            {
                case FieldInfo field:
                    return field.GetValue(EvaluateInstance(member.Expression, field.IsStatic));

                case PropertyInfo property:
                    var isStatic = property.GetMethod?.IsStatic ?? false;
                    return property.GetValue(EvaluateInstance(member.Expression, isStatic), BindingFlags.DoNotWrapExceptions, null, null, null);

                default:
                    return Microsoft.Web.Mvc.CachedExpressionCompiler.Evaluate(member);
            }
        }

        private static object? EvaluateNew(NewExpression create)
        {
            // a value type without a constructor: new S() is its default value
            if (create.Constructor == null) return GetDefault(create.Type);

            if (!CanInvoke(create.Constructor)) return Microsoft.Web.Mvc.CachedExpressionCompiler.Evaluate(create);

            return create.Constructor.Invoke(BindingFlags.DoNotWrapExceptions, null, EvaluateAll(create.Arguments), null);
        }

        private static object? EvaluateMemberInit(MemberInitExpression init)
        {
            // new Address { Street = "Main" }: the object, then its members in order. A value type is changed in its box.
            var instance = Evaluate(init.NewExpression)!;

            foreach (MemberAssignment assignment in init.Bindings)
            {
                var value = Evaluate(assignment.Expression);

                switch (assignment.Member)
                {
                    case FieldInfo field:
                        field.SetValue(instance, value);
                        break;

                    case PropertyInfo property:
                        property.SetValue(instance, value, BindingFlags.DoNotWrapExceptions, null, null, null);
                        break;

                    default:
                        throw new NotSupportedException($"Unexpected member in an initializer: {assignment.Member}");
                }
            }

            return instance;
        }

        private static object? EvaluateListInit(ListInitExpression list)
        {
            // new List<int> { 1, 2 }, new Dictionary<string, int> { { "one", 1 } }: the collection, then an Add per element
            var instance = Evaluate(list.NewExpression)!;

            foreach (var initializer in list.Initializers)
            {
                Invoke(initializer.AddMethod, instance, EvaluateAll(initializer.Arguments));
            }

            return instance;
        }

        private static Array EvaluateNewArray(NewArrayExpression array)
        {
            var result = Array.CreateInstance(array.Type.GetElementType()!, array.Expressions.Count);

            for (var i = 0; i < array.Expressions.Count; i++)
            {
                result.SetValue(Evaluate(array.Expressions[i]), i);
            }

            return result;
        }

        /// <summary>The instance of a member or a method, which must not be null, as a compiled call would throw.</summary>
        private static object? EvaluateInstance(Expression? instance, bool isStatic)
        {
            if (instance == null) return null;

            return Evaluate(instance) ?? (isStatic ? null : throw new NullReferenceException());
        }

        private static object?[] EvaluateAll(ReadOnlyCollection<Expression> expressions)
        {
            if (expressions.Count == 0) return [];

            var values = new object?[expressions.Count];

            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Evaluate(expressions[i]);
            }

            return values;
        }

        /// <summary>Calls the method as compiled code would: its exceptions aren't wrapped in a TargetInvocationException.</summary>
        private static object? Invoke(MethodInfo method, object? instance, object?[] args)
        {
            return method.Invoke(instance, BindingFlags.DoNotWrapExceptions, null, args, null);
        }

        /// <summary>A method that reflection can call with boxed values: no ref, out or in parameters, pointers, or ref structs.</summary>
        private static bool CanInvoke(MethodBase method)
        {
            // GetParameters copies its array: cached, an argument like TimeSpan.FromMinutes(5) is evaluated for every job
            return _invocableMethods.GetOrAdd(method, static m => IsInvocable(m));
        }

        private static bool IsInvocable(MethodBase method)
        {
            if (method is MethodInfo info && !CanBox(info.ReturnType) && info.ReturnType != typeof(void)) return false;

            foreach (var parameter in method.GetParameters())
            {
                if (!CanBox(parameter.ParameterType)) return false;
            }

            return true;
        }

        private static bool CanBox(Type type) => !type.IsByRef && !type.IsPointer && !type.IsByRefLike && !type.ContainsGenericParameters;

        /// <summary>A conversion that gives the same object: to a base type or an interface (boxing included), or to the Nullable of a value type.</summary>
        private static bool IsUnchangedByConversion(Type from, Type to)
        {
            if (from == to) return true;

            if (!to.IsValueType) return to.IsAssignableFrom(from);

            return Nullable.GetUnderlyingType(to) == from;
        }

        private static bool IsNullable(Type type) => Nullable.GetUnderlyingType(type) != null;

        /// <summary>default(T): null, or a value type's zeroed value (without calling a parameterless constructor).</summary>
        private static object? GetDefault(Type type)
        {
            return type.IsValueType && !IsNullable(type) ? RuntimeHelpers.GetUninitializedObject(type) : null;
        }

        #endregion

        #region Names

        /// <summary>The stored <c>TypeName</c>: <c>Namespace.Outer+Nested, Assembly</c>, <c>Namespace.Generic`1[[System.String, System.Private.CoreLib]], Assembly</c>.</summary>
        private static string GetTypeName(Type type)
        {
            var sb = new StringBuilder(128);

            AppendQualifiedTypeName(sb, type);

            return sb.ToString();
        }

        /// <summary>The type's name and its assembly's simple name, as <see cref="Type.GetType(string)"/> reads them.</summary>
        private static void AppendQualifiedTypeName(StringBuilder sb, Type type)
        {
            AppendTypeName(sb, type);

            sb.Append(", ").Append(type.Assembly.GetName().Name);
        }

        private static void AppendTypeName(StringBuilder sb, Type type)
        {
            if (type.HasElementType)
            {
                AppendTypeName(sb, type.GetElementType()!);
                AppendElementTypeSuffix(sb, type);
            }
            else if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                // the arguments in brackets, each one with its assembly: [[System.String, System.Private.CoreLib],[...]]
                sb.Append(type.GetGenericTypeDefinition().FullName).Append('[');

                var arguments = type.GetGenericArguments();

                for (var i = 0; i < arguments.Length; i++)
                {
                    if (i > 0) sb.Append(',');

                    sb.Append('[');
                    AppendQualifiedTypeName(sb, arguments[i]);
                    sb.Append(']');
                }

                sb.Append(']');
            }
            else
            {
                sb.Append(type.FullName ?? type.Name);
            }
        }

        /// <summary>A parameter's type in a signature: <c>System.Int32[]</c>, <c>System.Collections.Generic.Dictionary`2[System.String, System.Int32]</c>.</summary>
        private static void AppendParameterTypeName(StringBuilder sb, Type type)
        {
            if (type.HasElementType)
            {
                AppendParameterTypeName(sb, type.GetElementType()!);
                AppendElementTypeSuffix(sb, type);
            }
            else if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                sb.Append(type.GetGenericTypeDefinition().FullName).Append('[');

                var arguments = type.GetGenericArguments();

                for (var i = 0; i < arguments.Length; i++)
                {
                    if (i > 0) sb.Append(", ");

                    AppendParameterTypeName(sb, arguments[i]);
                }

                sb.Append(']');
            }
            else
            {
                // a generic parameter (T) has no full name
                sb.Append(type.FullName ?? type.Name);
            }
        }

        private static void AppendElementTypeSuffix(StringBuilder sb, Type type)
        {
            if (type.IsByRef)
            {
                sb.Append('&');
            }
            else if (type.IsPointer)
            {
                sb.Append('*');
            }
            else if (type.IsSZArray)
            {
                sb.Append("[]");
            }
            else
            {
                // a multidimensional array: [,] for two dimensions, [*] for one
                var rank = type.GetArrayRank();

                sb.Append('[').Append(rank == 1 ? "*" : new string(',', rank - 1)).Append(']');
            }
        }

        #endregion

        #region Run side

        private static MethodInfo FindMethod(Type type, string methodName, int paramCount)
        {
            var parenthesis = methodName.IndexOf('(');
            var name = parenthesis < 0 ? methodName : methodName[..parenthesis];

            MethodInfo? bySignature = null;

            // the methods with the name and the parameter count, by signature: a method hidden with new has the same one as the base method
            var byParameterCount = new Dictionary<string, MethodInfo>();

            foreach (var method in type.GetMethods(PublicMethods))
            {
                if (method.Name != name || method.IsGenericMethodDefinition) continue;

                var signature = GetMethodSignature(method);

                if (signature == methodName)
                {
                    bySignature = MostDerived(bySignature, method);
                }

                if (method.GetParameters().Length == paramCount)
                {
                    byParameterCount[signature] = MostDerived(byParameterCount.GetValueOrDefault(signature), method);
                }
            }

            if (bySignature != null) return bySignature;

            // a job without a signature (stored before 0.1.7), or with one that no longer matches: the only method with its name and parameter count
            return byParameterCount.Count switch
            {
                1 => byParameterCount.Values.Single(),

                0 => throw new MissingMethodException(
                    $"The job's method '{methodName}' wasn't found: the type '{type.FullName}' has no public method named '{name}' with {paramCount} parameter(s)."),

                _ => throw new AmbiguousMatchException(
                    $"The job's method '{methodName}' is ambiguous: the type '{type.FullName}' has several public methods named '{name}' with {paramCount} parameter(s)."),
            };
        }

        /// <summary>Of two methods with the same signature, the one declared on the more derived type.</summary>
        private static MethodInfo MostDerived(MethodInfo? current, MethodInfo candidate)
        {
            if (current == null) return candidate;

            return candidate.DeclaringType!.IsSubclassOf(current.DeclaringType!) ? candidate : current;
        }

        #endregion

        /// <summary>A method and the job type it's called on, compared by reference: reflection returns the same objects for them.</summary>
        private readonly struct JobMethodKey(MethodInfo method, Type jobType) : IEquatable<JobMethodKey>
        {
            public MethodInfo Method { get; } = method;
            public Type JobType { get; } = jobType;

            public bool Equals(JobMethodKey other) => ReferenceEquals(Method, other.Method) && ReferenceEquals(JobType, other.JobType);

            public override bool Equals(object? obj) => obj is JobMethodKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Method), RuntimeHelpers.GetHashCode(JobType));
        }

        private sealed class JobMethod(string typeName, string methodName, bool[] storedAsNull)
        {
            public string TypeName { get; } = typeName;
            public string MethodName { get; } = methodName;

            /// <summary>Per parameter: a CancellationToken or an XactJobContext, which the worker passes, stored as null.</summary>
            public bool[] StoredAsNull { get; } = storedAsNull;

            /// <summary>Validates the method, which then isn't checked again. A method that can't be a job throws, and isn't cached.</summary>
            public static JobMethod Create(MethodInfo method, Type jobType)
            {
                var description = $"{jobType.Name}.{method.Name}";

                if (!method.IsPublic)
                {
                    throw new NotSupportedException($"The job method {description} must be public.");
                }

                if (method.IsGenericMethod)
                {
                    throw new NotSupportedException($"The job method {description} can't be a generic method.");
                }

                if (method.ReturnType == typeof(void) && method.IsDefined(typeof(AsyncStateMachineAttribute), inherit: false))
                {
                    throw new NotSupportedException($"The job method {description} can't be async void: return a Task instead.");
                }

                var parameters = method.GetParameters();
                var storedAsNull = new bool[parameters.Length];

                for (var i = 0; i < parameters.Length; i++)
                {
                    var parameterType = parameters[i].ParameterType;

                    if (parameterType.IsByRef)
                    {
                        throw new NotSupportedException($"The job method {description} can't have out, ref or in parameters ({parameters[i].Name}).");
                    }

                    if (typeof(Delegate).IsAssignableFrom(parameterType))
                    {
                        throw new NotSupportedException($"The job method {description} can't have a delegate parameter ({parameters[i].Name}): its arguments are stored as JSON.");
                    }

                    storedAsNull[i] = parameterType == typeof(CancellationToken) || parameterType == typeof(XactJobContext);
                }

                return new JobMethod(GetTypeName(jobType), GetMethodSignature(method), storedAsNull);
            }
        }

        /// <summary>A JSON writer on its buffer, reset for each job.</summary>
        private sealed class ArgumentsWriter
        {
            public ArrayBufferWriter<byte> Buffer { get; } = new(256);

            private Utf8JsonWriter? _json;

            public Utf8JsonWriter Start()
            {
                Buffer.ResetWrittenCount();

                if (_json == null)
                {
                    _json = new Utf8JsonWriter(Buffer);
                }
                else
                {
                    _json.Reset(Buffer);
                }

                return _json;
            }
        }
    }
}
