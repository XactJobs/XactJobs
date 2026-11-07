// Jobs for SerializerFormatTests, in a namespace of their own: their type names are part of the expected strings.
namespace XactJobs.Tests.Golden;

public enum Priority
{
    Low,
    High = 5,
}

public sealed class Address
{
    public string Street { get; set; } = "";
    public int Number { get; set; }
    public string? Note { get; set; }
}

public sealed record Money(decimal Amount, string Currency);

public static class StaticJobs
{
    public static void NoArgs() { }
    public static void Primitives(string s, int i, long l, bool b, double d, decimal m, char c, byte by, short sh, float f, uint ui, ulong ul) { }
    public static void NullableValues(int? a, int? b, DateTime? c) { }
    public static void NullString(string? s) { }
    public static void Text(string s) { }
    public static void Times(DateTime utc, DateTime unspecified, DateTimeOffset offset, TimeSpan span, DateOnly date, TimeOnly time) { }
    public static void Ids(Guid id, Priority priority, DayOfWeek day) { }
    public static void Collections(int[] numbers, string[] names, List<string> list, Dictionary<string, int> map, IReadOnlyList<long> readOnly) { }
    public static void Params(params string[] items) { }
    public static void Objects(Address address, Money money, object boxed, Address? missing) { }
    public static Task WithTokenAsync(int id, CancellationToken cancellationToken) => Task.CompletedTask;
    public static Task WithContextAsync(XactJobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    public static void Overload(string s) { }
    public static void Overload(int i) { }
    public static void Overload(string s, int i) { }
    public static int Compute(int x) => x * 21;
}

public class InstanceJobs
{
    public void Run() { }
    public void Run(string name) { }
    public Task RunAsync(int id) => Task.CompletedTask;
    public Task<int> CountAsync() => Task.FromResult(1);
    public virtual void Virtual() { }
}

public class DerivedJobs : InstanceJobs
{
    public override void Virtual() { }
    public void Own() { }
}

public interface IJobService
{
    Task ProcessAsync(int id, CancellationToken cancellationToken);
}

public static class Outer
{
    public static class Nested
    {
        public static void Run(int x) { }
    }
}

public static class GenericHolder<T>
{
    public static void Run(int x) { }
}

public class GenericInstance<T>
{
    public void Run(T value) { }
}

public class InvalidJobs
{
    public void Generic<T>(T value) { }
    public void WithDelegate(Action action) { }
    public void WithFunc(Func<int> func) { }
#pragma warning disable VSTHRD100 // async void, on purpose: the serializer must reject it
    public async void AsyncVoid() => await Task.Yield();
#pragma warning restore VSTHRD100
    internal void NotPublic() { }
}

internal static class InternalJobs
{
    public static void Run() { }
}
