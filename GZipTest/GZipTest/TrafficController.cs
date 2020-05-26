using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Threading;
using GZipTest.Converters;

namespace GZipTest
{
    public class TrafficController
    {
        //потокобезопасная очередь для обмена блоками между потоком чтения и потоками конвертера
        private ThreadSafeQueue readBlocks = null;
        //потокобезопасная очередь для обмена блоками между потоками конвертера и потоками записи
        private ThreadSafeQueue blocks2Write = null;
        private Thread[] threads = new Thread[EnvironParameters.threadsCount];//массив потоков конвертера
        //массив событий-сигналов, используется для определени, что потоки отработали
        private AutoResetEvent[] signalsThreads = new AutoResetEvent[EnvironParameters.threadsCount];
        private AutoResetEvent[] signalsIO = new AutoResetEvent[2];
        //класс параметров - наименование исходного, результирующего файла, конвертер
        private OperatingParameters workParams = null;
        //флаг сигнализирующий о какой-то ошибке в работе
        private bool isSomethingWrong = false;
        //объект для замера затраченного времени на работу программы
        private Stopwatch stopWatch = new Stopwatch();

        public TrafficController(OperatingParameters wParams)
        {
            workParams = wParams;
        }

        //метод, инициализирующий очереди и запускающий потоки 
        public int Start()
        {
            if (workParams.coderEngine == null) return 0;

            //В отчёте для пользователя используется размер исходного файла, поэтому проверка осуществляется здесь, а не в catch метода чтения
            if (!File.Exists(workParams.srcFile))
            {
                Console.WriteLine("Файл {0} отсутствует по заданному пути", workParams.srcFile);
                return 0;
            }

            //Формируем сообщение для пользователя и записываем данные в лог-файл
            GenerateReportsTitle(); 

            //запускаем таймер замера времени
            stopWatch.Start();

            //инициализируем очереди, очередь на запись параметром получает флаг из текущего конвертера,
            //сигнализирующий требуется ли соблюдать порядок при загрузке или нет
            readBlocks = new ThreadSafeQueue();
            blocks2Write = new ThreadSafeQueue(workParams.coderEngine.useConsistent);

            //запускаем поток чтения исходного файла
            Thread reader = new Thread(ReadFStream) { Name = "Reader" };
            signalsIO[0] = new AutoResetEvent(false); //инициализируем экземпляр AutoResetEvent со стартовым состоянием false
            reader.Start();//Запускаем поток чтения

            //запускаем потоки конвертеров и создаём маркеры состояний для них
            for (int i = 0; i < EnvironParameters.threadsCount; i++)
            {
                threads[i] = new Thread(ConvertData) { Name = i.ToString()};
                signalsThreads[i] = new AutoResetEvent(false);
                threads[i].Start();
            }
            
            //запускаем поток записи в результирующий файл
            Thread writer = new Thread(WriteFStream) { Name = "Writer" };
            signalsIO[1] = new AutoResetEvent(false);
            writer.Start();

            //ждём сигналов от потоков конвертеров
            WaitHandle.WaitAll(signalsThreads);

            //вне зависимости от результатов работы потоков конвертеров
            //сообщаем очереди на запись, что больше поступлений блоков не будет
            blocks2Write.StopLoading();

            //ждём завершения потоков ввода-вывода
            WaitHandle.WaitAll(signalsIO);
            //останавливаем таймер замера времени выполнения
            stopWatch.Stop();
            //завершаем отчёт в консоль и в лог-файл
            GenerateReportsComplettion(isSomethingWrong); 

            //возращаем результат 
            return isSomethingWrong ? 0 : 1;
       }

        //метод, инициализирующий поток для чтения файла 
        private void ReadFStream()
        {
            try
            {
                using (FileStream fstream = new FileStream(workParams.srcFile, FileMode.Open))
                {
                    //подсчёт блоков используется для запоминания их очерёдности в исходном файле
                    uint readBlocksCount = 0;
                    //примерный подсчёт длины считанного блока данных
                    long bytesRead = 0;
                    Block block = null;
                    //Отображение процесса выполнения пришлось перенести сюда из метода записи, 
                    //потому-что только здесь есть параметр, по которому можно определить % выполнения
                    Console.Write("Процесс выполнения: ");

                    //непосредственное чтение данных из потока осуществляет функция архиватора, передаём ей обозначение потока и порядковый номер
                    while (!isSomethingWrong && ((block = workParams.coderEngine.GetBlockFromStream(fstream, readBlocksCount++)) != null))
                    {
                        //добавляем считанный блок в очередь
                        readBlocks.AddItem(block);
                        //считаем кол-во считанных байтов
                        bytesRead += block.srcArray.Length;
                        //Если не поступил сигнал остановить работу, то выводим текущий прогрес в консоль
                        if (!isSomethingWrong) Logger.ReportProgress(bytesRead, fstream.Length, readBlocksCount);
                    }
                    
                    //Если выход из цикла чтения произошёл по причинам завершения работы, 
                    //то сигнализируем в консоль
                    if (!isSomethingWrong)
                    {
                        Logger.ReportProgress(100, 100, readBlocksCount - 1); 
                        Logger.WriteLog("\nКоличество блоков: " + (readBlocksCount - 1));
                    }
                }
            }
            catch (Exception ex)
            {
                //если где-то возникла ошибка, сигнализируем пользователю в консоль и записываем в лог-файл
                isSomethingWrong = true;
                string error = "\nОшибка потока чтения: " + ex.Message;
                Console.WriteLine("\n" + error);
                Logger.WriteLog(error);
            }

            //сообщаем очереди чтения, что её заполнение завершено
            readBlocks.StopLoading();
            //подаём сигнал
            signalsIO[0].Set();
        }

        private void WriteFStream()
        {
            try
            {
                //открываем поток для файла записи
                using (FileStream fileOut = new FileStream(workParams.dscFile, FileMode.Create))
                {
                    while (!isSomethingWrong)
                    {
                        //запрашиваем у очереди блок
                        Block block = blocks2Write.GetItem();
                        if (block == null) break;
                        //пытаемся записать байт-массив
                        fileOut.Write(block.dstArray, 0, block.dstArray.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                isSomethingWrong = true;
                string error = "\nОшибка записи файла: " + ex.Message;
                Console.WriteLine("\n" + error);
                Logger.WriteLog(error);
            }
            //сигнализируем
            signalsIO[1].Set();
        }

        private void ConvertData()
        {
            try
            {
                while (!isSomethingWrong)
                {
                    //пытаемся получить блок из очереди чтения
                    Block bl = readBlocks.GetItem();
                    if (bl == null) break;
                    //передаём блок текущему конвертеру
                    workParams.coderEngine.Convert(bl);
                    //пытаемся передать блок в очередь на запись
                    blocks2Write.AddItem(bl);
                }
            }
            catch (Exception ex)
            {
                isSomethingWrong = true;
                string error = "\nОшибка конвертора: " + ex.Message;
                Console.WriteLine("\n" + error);
                Logger.WriteLog(error);
            }

            //сигнализируем
            signalsThreads[int.Parse(Thread.CurrentThread.Name)].Set();
        }

        //метод формирует титульную часть отчёта для пользователя и запись в лог файл
        private void GenerateReportsTitle()
        {
            long fileLen = new System.IO.FileInfo(workParams.srcFile).Length;
            string message = String.Format("{0} ({1} байт) -> {2}, метод {3} ({4})",
                workParams.srcFile, fileLen, workParams.dscFile, workParams.coderEngine.coderName,
                Enum.GetName(typeof(eCoderMethod), workParams.coderEngine.coderMethod).ToLower());

            Console.WriteLine(message);
            Logger.WriteLog(String.Format("\n\nДата: {0}, время: {1}\n{2}",
                DateTime.Now.ToShortDateString(), DateTime.Now.ToShortTimeString(), message));

            message = String.Format("Кол-во потоков архиватора {0}, разрядность среды {1}",
                EnvironParameters.threadsCount, EnvironParameters.bitDepth);

            Console.WriteLine(message);
            Logger.WriteLog("\n" + message);

            Logger.WriteLog("\nОбъём свободной физической памяти: "
                + EnvironParameters.memStatus.ullAvailPhys + " байт");
            Logger.WriteLog("\nОграничение на размер очереди: "
                + EnvironParameters.GetQueueBlocksLimit() + " элементов");
        }
        //метод формирует заключительную часть отчёта для пользователя о проделанной работе и
        //затраченном времени + запись в лог-файл
        private void GenerateReportsComplettion(bool isAborted)
        {
            TimeSpan ts = stopWatch.Elapsed;
            string message = String.Format("\nЗатраченное время : {0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
            Console.WriteLine(message);
            Logger.WriteLog(message + "\nРезультат: " + (isAborted ? "Отмена" : "Успешно"));
        }
    }
}
