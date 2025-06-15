namespace XactJobs.TestModel
{
    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }

        public User(int id, string firstName, string lastName, string email)
        {
            Id = id;
            FirstName = firstName;
            LastName = lastName;
            Email = email;
        }

        public static void MyJob(int id, string name, Guid guid)
        {
            Console.WriteLine(id);
            Console.WriteLine(name);
            Console.WriteLine(guid);
        }

        public static async Task MyJobAsync(int id, string name, Guid guid, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            Console.WriteLine(id);
            Console.WriteLine(name);
            Console.WriteLine(guid);
        }
    }
}
