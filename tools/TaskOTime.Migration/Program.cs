namespace TaskOTime.Migration;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await Migration.RunAsync(args);
    }
}
