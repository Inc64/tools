using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using GZipTest.Converters;

namespace GZipTest
{
    public class OperatingParameters
    {
        private string defaultCoderName = null;
        private List<IBytesConverter> coderArray = null;
        
        public string srcFile { get; private set; }
        public string dscFile { get; private set; }
        public IBytesConverter coderEngine { get; private set; }
        
        public OperatingParameters(string[] args)
        {
            //параметры коммандной строки
            //парсер считает обязательными compress/decompress [имя исходного файла]
            //при неуказании имени результирующего файла, он его создаст добавив расширение, соответствующее
            //наименованию архиватора к исходному файлу

            //реализованные архиваторы
            coderArray = new List<IBytesConverter>()
                        {
                            new GzipCompressorAsync(), new GzipDecompressorAsync(),
                            new GzipCompressor(), new GzipDecompressor()
                        };

            
            if (args.Count() < 2)
            {
                Console.WriteLine(GetInstructions4User());
                return;
            }

            defaultCoderName = coderArray[0].coderName;

            //считываем compress/decompress
            string methodName = args[0].ToLower();
            eCoderMethod coderMethod = eCoderMethod.Compress;
            switch (methodName)
            {
                case "compress": coderMethod = eCoderMethod.Compress; break;
                case "decompress": coderMethod = eCoderMethod.Decompress; break;
                default:
                    {
                        coderEngine = null;
                        Console.WriteLine(GetInstructions4User());
                        return;
                    }
            }

            //считываем наименование исходного файла
            srcFile = args[1];

            //если аргументов всего 2, выставляем архиватор по умолчанию GzipCompressorAsync/GzipDecompressorAsync
            if (args.Count() == 2)
            {
                if ((coderEngine = (from cdr in coderArray
                                    where cdr.coderName == defaultCoderName && cdr.coderMethod == coderMethod
                                    select cdr).FirstOrDefault()) == null)
                {
                    Console.WriteLine("Не удалось найти архиватор по умолчанию");
                    return;
                }
                //т.к. параметров 2, то результирующий файл не указан - пытаемся придумать
                dscFile = GetDestFileName();
                return;
            }

            //параметров 3
            //проверяем, может быть 3м параметром указан не результирующий файл, а наименование кодера
            string coderName = TryParseCoderName(args[2]);

            if (String.IsNullOrWhiteSpace(coderName))
            {
                //значит, файл
                dscFile = args[2];

                //может кодер указан 4м параметром?
                coderName = args.Count() > 3 ? TryParseCoderName(args[3]) : defaultCoderName;
            }

            //получаем объект по названию кодера и функционалу (compress/decompress)
            coderEngine = (from cdr in coderArray
                           where cdr.coderName == coderName && cdr.coderMethod == coderMethod
                           select cdr).FirstOrDefault();

            if (coderEngine == null)
            {
                Console.WriteLine(GetInstructions4User());
                return;
            }

            dscFile = String.IsNullOrWhiteSpace(dscFile) ? GetDestFileName() : dscFile;
        }

        private string TryParseCoderName(string s)
        {
            if (s.StartsWith("\\"))
            {
                s = s.Replace("\\", "");
                return String.IsNullOrWhiteSpace(s) ? defaultCoderName : s;
            }
            return null;
        }
        //вывод в консоль инстукции для пользователя
        private string GetInstructions4User()
        {
            string methods = "отсутствуют";
            string app = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            string sm = null;
            try
            {
                sm = String.Join(", ", coderArray.Select(item => item.coderName).ToList().Distinct().ToArray());
                methods = sm ?? methods;

            }
            catch { }

            return "Формат входных параметров:\n\n" + app +
                " compress/decompress \"имя исходного файла\" [\"имя результирующего файла\"] [\\\"метод архивирования\"]\n\n" +
                "   Параметры, указанные в скобках [] - не обязательные\n" +
                "   Доступные методы архивирования : " + methods +
                " (по умолчанию выбирается метод архивирования: agz)\n";
        }
        //формирование наименования результирующего файла, если тот не указан
        private string GetDestFileName()
        {
            if (String.IsNullOrWhiteSpace(srcFile)) return null;

            if (coderEngine.coderMethod == eCoderMethod.Decompress)
            {
                return Path.GetFileNameWithoutExtension(srcFile) + ".file";
            }
            return String.Format("{0}.{1}", srcFile, coderEngine.coderName);
        }
    }

    //клас содержащий некоторые ТТХ локальной станции, используется для контроля за выделением памяти
    public static class EnvironParameters
    {
        private static MEMORYSTATUSEX memSingleton = null;

        //Эмпирическим путём получено, что быстрее работает, когда кол-во 
        //потоков конвертера = к-ву логических CPU, а не значению NumberOfCores в Win32_Processor
        private static int numberLogicCPU = Environment.ProcessorCount;
        private static ulong memSizeLimitx86 = 524288000;  //1024 * 1024 * 500;
        private static ulong memSizeLimitx64 = 2097152000; //1024 * 1024 * 2000;
        
        //Т.к.цикличность работы через консоль не подразумевается, то 
        //опрос о состоянии памяти делаем один раз в момент первого обращения
        public static MEMORYSTATUSEX memStatus
        {
            get
            {
                if (memSingleton == null)
                {
                    memSingleton = new MEMORYSTATUSEX();
                    EnvironParameters.GlobalMemoryStatusEx(memSingleton);
                }
                return memSingleton;
            }
        }
        public static uint   blockLength = 1048576; //1024 * 1024;
        public static int    threadsCount = numberLogicCPU;
        //разрядность локальнйо станции
        public static string bitDepth
        {
            get 
            { 
                return Environment.Is64BitProcess ? "x64(64 bits)" : "x86(32 bits)"; 
            }
        }

        //ограничение на размер очереди
        public static ulong GetQueueBlocksLimit()
        {
            return GetMemAllocLimit() / EnvironParameters.blockLength;
        }
        //Ограничения по размеру выделяемой памяти, 
        //для win32 из расчета, чтобы не поймать OutOFMemory, т.к. ограничение на размер в памяти любого процесса 
        //вместе со стэком данных(1гб) = 2 гб (а практический лимит 1.75)
        //для x64 из расчёта чтобы сильно не напрягать Garbage collector, эмпирическим путём можно скорректировать
        //рабочим параметром берётся минимум из ограничений и кол-ва имеющейся свободной памяти на момент запуска
        public static ulong GetMemAllocLimit()
        {
            ulong avmem = EnvironParameters.memStatus.ullAvailPhys;
            ulong limitx86 = EnvironParameters.memSizeLimitx86;
            ulong limitx64 = EnvironParameters.memSizeLimitx64;
            return Environment.Is64BitProcess ? Math.Min(avmem, limitx64) : Math.Min(avmem, limitx86);
        }
        //используется для чтения массивов данных из потока Stream, когда длину массива тоже требуется прочитать
        //из файла. Если пользователь случайно укажет файл другого формата, то мы можем попытаться 
        //инициализировать массив байтов размерностью в uint.MaxValue и получить OutOfMemory на выходе
        public static bool CheckMemAllocLimit(uint length)
        {
            //должно быть странным, если мы пытаемся alloc массив значительно больше, чем blockLength
            return length < Math.Min(GetMemAllocLimit(), blockLength << 2);
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength { get; set; }
        public uint dwMemoryLoad { get; set; } //% используемой в данный момент памяти
        public ulong ullTotalPhys { get; set; } //сколько всего физ. памяти установлено
        public ulong ullAvailPhys { get; set; } //объём доступной физ.памяти в байт - это значение и будет использоваться
        public ulong ullTotalPageFile { get; set; }//макс предел выделяемой памяти 
        public ulong ullAvailPageFile { get; set; }//макс предел памяти, которую можно выделить под процесс 
        public ulong ullTotalVirtual { get; set; }//макс виртуальное адрессное пространствово (2гб для x86)
        public ulong ullAvailVirtual { get; set; }//свободное вирт.адр.пр-во
        public ulong ullAvailExtendedVirtual { get; set; } //0

        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }
}
