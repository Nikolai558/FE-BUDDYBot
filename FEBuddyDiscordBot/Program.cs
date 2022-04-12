namespace FEBuddyDiscordBot;


class Program
{
    public static void Main(string[] args)
    {
        try
        {
            new FeBuddyBot().StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }
}
