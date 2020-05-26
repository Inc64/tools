using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace GZipTest.Converters
{
    public enum eCoderMethod
    {
        Compress,
        Decompress
    }

    //интерфейс класса отвечающего за преобразования байт массива
    public interface IBytesConverter
    {
        eCoderMethod coderMethod { get; } //метод

        //Флаг, сигнализирующий о том, нужно ли упорядычивать блоки
        // TRUE  -> при формировании исходной последовательности требуется сделать order by d_Index
        // FALSE -> порядок не важен
        bool useConsistent { get; }

        //наименование архиватора "gz,zip,rar,arj,ice", оно же используется в качестве расширения файла
        string coderName { get; }

        //Макет метода преобразования байт массива - сжатие,расжатие
        void Convert(Block block);

        //Макет метода чтения блока из потока
        Block GetBlockFromStream(Stream stream, uint defBlockIndex);
    }
}
