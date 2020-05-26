using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace GZipTest.Converters
{
    public class GzipDecompressor : GzipDecompressorAsync
    {
        public override string coderName { get { return @"gz"; } }

        //Функция извлечения блока из потока 
        public override Block GetBlockFromStream(Stream stream, uint defBlockIndex)
        {
            string error = "формат исходного файла не соответствует GZIP";
            if (stream == null) throw new Exception("не удалось прочитать файл");

            int count = 0;

            //Считываем преффикс блока (4 байта: ID1 = 31 (0x1f, \037) + ID2 = 139 (0x8b, \213) + CM + FLG)
            byte[] bufferPref = new byte[4];
            count = stream.Read(bufferPref, 0, 4);
            if (count == 0) return null;
            if (count != 4) throw new Exception(error);
            if (bufferPref[0] != 31 || bufferPref[1] != 139) throw new Exception(error);

            //Считываем MTIME
            byte[] bufferLen = new byte[4];
            count = stream.Read(bufferLen, 0, 4);
            if (count != 4) throw new Exception(error);
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
            if (count != arrayLen - 8) throw new Exception(error);

            //Возвращаем результат
            return new Block(defBlockIndex, arrayData);
        }
    }
}
