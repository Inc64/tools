using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GZipTest
{
    public class Block
    {
        //порядковый номер блока
        public uint index { get; private set; }
        //исходный массив байт
        public byte[] srcArray { get; private set; }
        //результирующий массив байт
        public byte[] dstArray { get; set; }

        public Block(uint index, byte[] src)
        {
            this.index = index;
            this.srcArray = src;
        }
    }
}
