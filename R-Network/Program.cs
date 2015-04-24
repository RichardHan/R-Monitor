using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace R_Network
{
    class Program
    {
        static int sleepSecs = string.IsNullOrEmpty(ConfigurationManager.AppSettings["sleepSecs"]) ?
           3 : int.Parse(ConfigurationManager.AppSettings["sleepSecs"]);

        static void Main(string[] args)
        {

            while (true)
            {

                bool network = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();

                if (network)
                {
                    Console.WriteLine("[Up]");
                }
                else
                { writeErrorLog(); }

                Thread.Sleep(sleepSecs * 1000);
            }
        }

        private static void writeErrorLog()
        {
            //write log to scv.
            using (StreamWriter outfile = new StreamWriter(@"NetworkLogs.csv", true))
            {
                outfile.WriteLine(DateTime.UtcNow.AddHours(8).ToString("G"));
            }

            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Down]");
            Console.ResetColor();
        }
    }
}
