using XactJobs;

namespace TestConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var job = DbContextExtensions.Enqueue(null, () => MyJob(1, "test job", Guid.NewGuid()));
        }


        public static void MyJob(int id, string name, Guid guid)
        {
            Console.WriteLine(id);
            Console.WriteLine(name);
            Console.WriteLine(guid);
        }
    }
}
