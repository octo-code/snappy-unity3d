using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;

namespace Snappy
{
    abstract class NativeProxy
    {
        public static readonly NativeProxy Instance = IntPtr.Size == 4 ? (NativeProxy)new Native32() : new Native64();

        protected NativeProxy(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var folder = Path.Combine(Path.GetTempPath(), "Snappy.NET-" + assembly.GetName().Version.ToString());
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            byte[] contents;
            using (var input = assembly.GetManifestResourceStream("Snappy." + name))
            using (var buffer = new MemoryStream())
            {
                byte[] block = new byte[4096];
                int copied;
                while ((copied = input.Read(block, 0, block.Length)) != 0)
                    buffer.Write(block, 0, copied);
                buffer.Close();
                contents = buffer.ToArray();
            }
            if (!File.Exists(path) || !Utils.BuffersEqual(File.ReadAllBytes(path), contents))
            {
                using (var output = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    output.Write(contents, 0, contents.Length);
            }
            var h = LoadLibrary(path);
            if (h == IntPtr.Zero)
                throw new ApplicationException("Cannot load " + name);
        }

        public unsafe abstract SnappyStatus Compress(byte* input, int inLength, byte* output, ref int outLength);
        public unsafe abstract SnappyStatus Uncompress(byte* input, int inLength, byte* output, ref int outLength);
        public abstract int GetMaxCompressedLength(int inLength);
        public unsafe abstract SnappyStatus GetUncompressedLength(byte* input, int inLength, out int outLength);
        public unsafe abstract SnappyStatus ValidateCompressedBuffer(byte* input, int inLength);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibrary(string lpFileName);
    }
}
