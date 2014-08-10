using System;
using System.Linq;


namespace UTDScanner
{
    class Program
    {

        static void Main(string[] args)
        {
            if (args.Count() > 0 && args[0].Equals("/all", StringComparison.CurrentCultureIgnoreCase))
            {
                Parser.Parse(true);
            }
            else if (args.Count() > 0 && args[0].Equals("/post", StringComparison.CurrentCultureIgnoreCase))
            {

            }
            else
            {
                Parser.Parse(false);
            }
        }
    }
}
