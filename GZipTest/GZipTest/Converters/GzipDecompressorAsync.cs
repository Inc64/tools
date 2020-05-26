using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;

namespace GZipTest.Converters
{
    public class GzipDecompressorAsync : IBytesConverter
    {
        //В данном конвертере порядок блоков важен -> Очередь must be ordered by Block.index на входе
        public virtual bool useConsistent { get { return true; } }

        public virtual string coderName { get { return @"agz"; } }

        public eCoderMethod coderMethod { get { return eCoderMethod.Decompress; } }

        public void Convert(Block block)
        {
            using (MemoryStream smstream = new MemoryStream(block.srcArray))
            using (GZipStream gzip = new GZipStream(smstream, CompressionMode.Decompress))
            using (MemoryStream dmstream = new MemoryStream())
            {
                gzip.CopyTo(dmstream);
                block.dstArray = dmstream.ToArray();
            }
        }

        public virtual Block GetBlockFromStream(Stream stream, uint defBlockIndex)
        {
            if (stream == null) throw new Exception("не удалось прочитать файл");
            string error = "формат исходного файла не соответствует AGZIP";
            int count = 0;
            
            //Считываем 4 байта - индекс
            byte[] bufferInd = new byte[4];
            count = stream.Read(bufferInd, 0, 4);
            if (count == 0) return null;
            if (count != 4)
            {
                throw new Exception(error);
            }
            if (BitConverter.IsLittleEndian) Array.Reverse(bufferInd);
            uint index = BitConverter.ToUInt32(bufferInd, 0);

            //Считываем преффикс блока (4 байта: ID1 = 31 (0x1f, \037) + ID2 = 139 (0x8b, \213) + CM + FLG)
            byte[] bufferPref = new byte[4] { 31, 139, 0, 0 };
            count = stream.Read(bufferPref, 2, 2);
            if (count != 2)
            {
                throw new Exception(error);
            }
            //Считываем 4 байта области MTIME
            byte[] bufferLen = new byte[4];
            count = stream.Read(bufferLen, 0, 4);
            if (count != 4)
            {
                throw new Exception(error);
            }
            //Проверяем на соответствие стандарту RFC1014 и получаем длину записанного блока
            if (BitConverter.IsLittleEndian) Array.Reverse(bufferLen);
            uint arrayLen = BitConverter.ToUInt32(bufferLen, 0);
            ulong allocLimit = EnvironParameters.GetMemAllocLimit();
            if (arrayLen >= allocLimit) throw new Exception("размер массива слишком большой, возможно нарушен формат входного файла");

            //Выделяем массив с заданной размерностью для блока и копируем в начало префикс
            byte[] arrayData = new byte[arrayLen];
            bufferPref.CopyTo(arrayData, 0);

            //Считываем туда наш блок начиная с сегмента XFL (т.е. с 8го байта)
            count = stream.Read(arrayData, 8, (int)arrayLen - 8);
            if (count != arrayLen - 8)
            {
                throw new Exception(error);
            }
            //Возвращаем результат
            return new Block(index, arrayData);
        }
    }
}
