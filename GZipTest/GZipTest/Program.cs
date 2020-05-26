using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GZipTest
{
    class Program
    {
        static void Main(string[] args)
        {
            //args = new string[] { "compress",   "test.xml", "test.agz" };
            //args = new string[] { "decompress", "test.agz", "test.xml" };
            //args = new string[] { "compress",   "test.xml", "test.gz",  "\\gz" };
            //args = new string[] { "decompress", "test.gz",  "test.xml", "\\gz" };

            Console.CursorVisible = false;

            int result = (new TrafficController(new OperatingParameters(args))).Start();
            
            Console.WriteLine("Результат работы приложения: " + result);
            Console.WriteLine("Нажмите любую клавишу для выхода");

            Console.CursorVisible = true;
            Console.ReadKey();
        }
    }
}
