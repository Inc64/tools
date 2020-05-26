using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

namespace GZipTest
{
    public static class Logger
    {
        private static object locker = new object();
        private static string fileNameLog = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + "\\logger.log";

        public static void WriteLog(string sOut)
        {
            Monitor.Enter(locker);
            try
            {
                if (!File.Exists(fileNameLog)) File.WriteAllText(fileNameLog, sOut);
                else File.AppendAllText(fileNameLog, sOut);
            }
            finally
            {
                Monitor.Exit(locker);
            }
        }

        public static void ReportProgress(long bw, long ba, uint i)
        {
            Console.CursorLeft = 20;
            Console.Write("{0}% (блоки {1})", bw * 100 / ba, i);
        }
    }
}
