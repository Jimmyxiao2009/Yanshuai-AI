// RefTensorFile.cs — 读取 dump_reference.py 写出的 RFT1 二进制（对齐测试用）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yanshuai.Qwen
{
    public sealed class RefTensor
    {
        public int[] Shape;
        public float[] Data;
        public int Count => Data.Length;
    }

    /// <summary>
    /// RFT1 格式：
    ///   "RFT1" | u32 num | { u32 nameLen, name(utf8), u32 ndim, dims[ndim](u32), f32 data[Πdims] } ×num
    /// </summary>
    public static class RefTensorFile
    {
        public static Dictionary<string, RefTensor> Load(string path)
        {
            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                var magic = br.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != (byte)'R' || magic[1] != (byte)'F'
                    || magic[2] != (byte)'T' || magic[3] != (byte)'1')
                    throw new InvalidDataException("RefTensorFile: bad magic");

                int n = br.ReadInt32();
                var dict = new Dictionary<string, RefTensor>(n);
                for (int i = 0; i < n; i++)
                {
                    int nameLen = br.ReadInt32();
                    string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
                    int ndim = br.ReadInt32();
                    var shape = new int[ndim];
                    long count = 1;
                    for (int j = 0; j < ndim; j++) { shape[j] = br.ReadInt32(); count *= shape[j]; }
                    var data = new float[count];
                    byte[] raw = br.ReadBytes((int)(count * 4));
                    Buffer.BlockCopy(raw, 0, data, 0, raw.Length);
                    dict[name] = new RefTensor { Shape = shape, Data = data };
                }
                return dict;
            }
        }
    }
}
