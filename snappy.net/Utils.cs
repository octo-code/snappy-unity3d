using System;
using System.Collections.Generic;
using System.Text;

namespace Snappy
{
    class Utils
    {
        public static bool BuffersEqual(byte[] left, byte[] right)
        {
            return left.Length == right.Length && BuffersEqual(left, right, left.Length);
        }

        public static bool BuffersEqual(byte[] left, byte[] right, int count)
        {
            for (int i = 0; i < count; ++i)
                if (left[i] != right[i])
                    return false;
            return true;
        }
    }
}
