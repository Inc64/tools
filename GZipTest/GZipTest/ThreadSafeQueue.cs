using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace GZipTest
{
    public class ThreadSafeQueue
    {
        private Queue<Block> blocks = new Queue<Block>();
        private bool isIndexed = false; //флаг = TRUE если требуется соблюдать порядок при загрузке очереди
        private uint countBlocks = 0; //счетчик кол-ва элементов в очереди
        private uint nextIndex = 0; //используется для последовательной загрузки индексированных данных в очередь

        //Фактическое максимальное значение элементов очереди и элементов в буфере
        //использовалось для log-файла при тестировании
        private uint countBlocksMax = 0;
        private int countBufferMax = 0;

        //Т.к. используется 2 очереди не сбалансированные по скорости загрузки-разгрузки из-за операций IO
        //Очередь чтения загружается 1 потоком, а выгружает >= 1го потока, т.е. накопительный рост этой очереди возможен
        //при некоторых условиях, но маловероятен
        //Очередь записи - самая медленная на разгрузку, т.к. её загружает >= 1 потоков, а разгружает 1, ограниченный скоростью
        //работы конкретного диска на запись. Если потоков несколько, то скорость роста очереди записи частично сдерживается ожиданиями
        //потоков очередной порции данных из очереди чтения, но тем не менее рост этой очереди при больших объемов файлов
        //может привести к OutOfMemory при достаточно медленных HDD. 
        //Или например если исходный файл берётся с SSD, а результирующий - пишется на HDD
        //Воспользовавшись эмперическими данными и данными из:
        //https://blogs.msdn.microsoft.com/tom/2008/04/10/chat-question-memory-limits-for-32-bit-and-64-bit-processes/
        //введём параметр, ограничивающий длину очереди для x86 и для x64, определив какая среда исполнения 
        //присутствует у юзера
        private ulong blocksLimit = 0;

        //Реализовано 2 варианта сжатия по алгоритму Gzip, подробнее см. GzipCompressor.cs и GzipCompressorAsync.cs
        //В реализации 2го сжатые блоки обрабатываются и записываются абсолютно независимо от других, тем самым порядок следования
        //в исходном файле не соблюдается, вместо этого порядковый номер блока в исходном файле добавляется в качестве доп.
        //информации в блок. Таким образом при обратной работе алгоритма необходимо восстанавливать исходный порядок блоков
        //и может возникнуть ситуация, что у текущих работающих потоков не будет нужного блока, который должен идти следующим
        //в очереди по индексу. Поэтому было принято решение добавить буфер типа SortedList, реализованный в System.Collections.Generic
        //как 2 массива фиксированной длины, операция сортировки проводится с помощью Array.BinarySearch<TKey> на стадии добавления каждого элемента
        //Энергозатратность буфера нивелируется тем, что в данной задаче размерность его требуется несущественной
        private SortedList<uint, Block> backBuffer = new SortedList<uint, Block>();
        public bool isFinished { get; private set; } //флаг сообщающий, что в очередь новые элементы добавляться больше не будут
        public bool isEmpty
        {
            get { return !blocks.Any(); }
        }

        public ThreadSafeQueue(bool useIndexes = false)
        {
            isIndexed = useIndexes;
            isFinished = false;

            //выставляем ограничение на размер очереди исходя из разрядности,
            //максимальному стеку приложения в win32 для x86, ограничениям на размер объектов памяти у CLI для всех,
            //и количества физ.памяти на данный момент, которую можно использовать
            //(на скорость работы это никак не влияет, т.к. если где-то растёт очередь, то скорее всего
            //где-то не справляется один маленький writer 
            //некоторая страховка от возможного Exception Out of Memory, особенно на x86
            blocksLimit = EnvironParameters.GetQueueBlocksLimit();

        }

        //добавление объекта в очередь
        public void AddItem(Block block)
        {
            Monitor.Enter(blocks);//блокируем объект blocks класса Queue<T> от доступа со стороны других потоков
            try
            {
                //Достигли ограничения по размеру очереди - ждём разгрузки
                while (countBlocks > blocksLimit)
                {
                    Monitor.Wait(blocks);
                }

                //Если требуется соблюдать порядок следования в очереди
                if (isIndexed)
                {
                    //Если индекс текущего блока не равен требуемому индексу, добавляем блок в буфер, освобождая поток
                    //и заодно сигнализируем всем потокам проверить состояние(особенно writer`у)
                    if (nextIndex != block.index)
                    {
                        backBuffer.Add(block.index, block);
                        countBufferMax = Math.Max(backBuffer.Count(), countBufferMax);
                        Monitor.PulseAll(blocks);
                        return;
                    }

                    blocks.Enqueue(block);
                    nextIndex++;
                    countBlocks++;
                    //Если поток добавил что-то в очередь, проверяем - не можем ли мы добавить туда что-то из буфера
                    while (backBuffer.Values.Count > 0 && backBuffer.Values[0].index == nextIndex)
                    {
                        blocks.Enqueue(backBuffer.Values[0]);
                        backBuffer.RemoveAt(0);
                        nextIndex++;
                        countBlocks++;
                    }

                    countBlocksMax = Math.Max(countBlocks, countBlocksMax);//для лог-файла
                    Monitor.PulseAll(blocks);//сигнализируем всем потокам об изменении состояния объекта
                    return;
                }

                //добавляем элемент
                blocks.Enqueue(block);
                countBlocks++;
                countBlocksMax = Math.Max(countBlocks, countBlocksMax);//для лог-файла

                //Сообщаем всем ожидающим об изменении состояния
                Monitor.PulseAll(blocks);
            }
            finally
            {
                Monitor.Exit(blocks); //освобождаем объект от блокировки
            }
        }

        public Block GetItem()
        {
            Monitor.Enter(blocks);//блокируем
            try
            {
                //Если работа по заполнению очереди идёт(т.е. !IsFinished), но очередь на момент обращения ещё пуста
                //то просто ждём освобождая все потоки от лока, пока не появится первый сигнал о локе
                while (isEmpty && !isFinished)
                {
                    Monitor.Wait(blocks);
                }
                //Если очередь не пуста, то достаём элемент
                if (!isEmpty)
                {
                    countBlocks--;
                    Monitor.PulseAll(blocks);//сигнализируем всем ожидающим об изменении состояния blocks
                    return blocks.Dequeue();
                }
            }
            finally
            {
                Monitor.Exit(blocks);//освобождаем blocks от блокировки
            }
            return null;
        }

        public void StopLoading()
        {
            //Поступил сигнал о прекращении загрузки очереди, т.к. он может поступить 
            //от нескольких потоков, то ставим блокировку
            Monitor.Enter(blocks);
            try
            {
                isFinished = true;
                //Если не уведомить все потоки, кто-то может застрять на Monitor.Wait(blocks)
                Monitor.PulseAll(blocks);
            }
            finally
            {
                Monitor.Exit(blocks);//освобождаем
            }
            string mes = String.Format("\nМакс.элементов очереди: {0}, макс.элементов буфера: {1}", countBlocksMax, countBufferMax);
            Logger.WriteLog(mes);
        }
    }
}
