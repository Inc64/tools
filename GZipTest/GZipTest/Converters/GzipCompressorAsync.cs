using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;

namespace GZipTest.Converters
{
    public class GzipCompressorAsync : IBytesConverter
    {
        public virtual bool useConsistent { get { return false; } } //при сжатии порядок бай-блоков не важен

        public virtual string coderName { get { return @"agz"; } }

        public eCoderMethod coderMethod { get { return eCoderMethod.Compress; } }

        public virtual void Convert(Block block)
        {
            //создаём поток в памяти для работы с GZipStream
            using (MemoryStream mstream = new MemoryStream())
            {
                //создаём поток GZipStream использующий mstream
                using (GZipStream gzip = new GZipStream(mstream, CompressionMode.Compress))
                {
                    //записываем в GZipStream массив байтов указанной размерности
                    gzip.Write(block.srcArray, 0, block.srcArray.Length);
                }

                //копируем преобразованные данные в массив res 
                byte[] res = mstream.ToArray();
                //Создаём массив dstArray, инициализировав его размер на 2 байта
                //в большую сторону от размера res
                block.dstArray = new byte[res.Length + 2];
                //и копируем в него данные из res, начиная со 2го байта
                res.CopyTo(block.dstArray, 2);
            }

            //Записываем длину массива байтов в сегмент MTIME 
            byte[] bytes = BitConverter.GetBytes(block.dstArray.Length - 2);
            //Проверка на соответствие стандарту RFC1014 https://tools.ietf.org/html/rfc1014
            //на случай если распаковка будет на локальной станции, реализующей другой стандарт
            //т.к. сам C# не определяет порядок байтов
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            bytes.CopyTo(block.dstArray, 6);

            //Порядковый индекс (4 байта) байт-массива в исходном файле записываем в начало 
            //получившегося массива, затирая тем самым байты ID1 и ID2
            bytes = BitConverter.GetBytes(block.index);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            bytes.CopyTo(block.dstArray, 0);
        }

        public Block GetBlockFromStream(Stream stream, uint defBlockIndex)
        {
            //выделяем буфер заданного размера defBlockLen
            uint defBlockLen = EnvironParameters.blockLength;
            byte[] buffer = new byte[defBlockLen];
            int count = 0;

            //считываем в него порцию данных, запрашивая их из потока в количестве defBlockLen
            if ((count = stream.Read(buffer, 0, (int)defBlockLen)) > 0)
            {
                //выделяем массив размерности == реально считанному кол-ву из потока данных
                byte[] array = new byte[count];
                //копируем в него считанные данные, манипуляция для того, 
                //чтобы размерность массива Block.srcArray соответствовала хранимым в нём данным
                Array.Copy(buffer, array, count);
                //создаём блок и возвращаем его заказчику
                return new Block(defBlockIndex, array);
            }
            return null;
        }
    }
}
