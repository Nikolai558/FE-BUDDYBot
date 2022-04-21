namespace FEBuddyDiscordBot;

/// <summary>
/// Main entrypoint Class for program start
/// </summary>
class Program
{
    /// <summary>
    /// The Main method is the entry point of an executable program; it is where the program control starts and ends.
    /// </summary>
    public static void Main()
    {
        // This will catch any exceptions thrown by synchronous code.
        try
        {
            // Create and start the FeBuddy Discord Bot
            new FeBuddyBot().StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Log the exception and crash the program.
            Console.WriteLine(ex.Message);
            throw;
        }
    }
}
