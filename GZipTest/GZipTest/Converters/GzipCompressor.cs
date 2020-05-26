using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;

namespace GZipTest.Converters
{
    public class GzipCompressor : GzipCompressorAsync
    {
        public override bool useConsistent { get { return true; } }

        public override string coderName { get { return @"gz"; } }

        public override void Convert(Block block)
        {
            using (MemoryStream mstream = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(mstream, CompressionMode.Compress))
                {
                    gzip.Write(block.srcArray, 0, block.srcArray.Length);
                }
                block.dstArray = mstream.ToArray(); ;
            }

            byte[] bytes = BitConverter.GetBytes(block.dstArray.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            bytes.CopyTo(block.dstArray, 4);
        }
    }

}
