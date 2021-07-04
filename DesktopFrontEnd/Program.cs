using System;

namespace DesktopFrontEnd
{
    /// <summary>
    /// The main class.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("No ROM file has been specified as a parameter.");
                return;
            }

            using (var game = new LeBoyGame(args[0]))
                game.Run();
        }
    }
}
