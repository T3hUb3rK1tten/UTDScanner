using System;
using System.Linq;
using System.Threading;


namespace UTDScanner
{
    class Program
    {

        static void Main(string[] args)
        {
            Console.WriteLine("Startup at " + DateTime.Now.ToString());
            // On startup, process every file to double check
            Parser.Parse(true);

            while(true)
            {
                var timer = new System.Threading.Timer(new TimerCallback(t => {
                    // Only process recent files
                    Parser.Parse(false);
                }));

                timer.Change(new TimeSpan(0, 30, 0), new TimeSpan(0, 30, 0));

                Thread.Sleep(Timeout.Infinite);
            }

        }
    }
}
