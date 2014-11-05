using System;
using System.Collections.Generic;
using System.Text;

namespace Snappy
{
    /// <summary>
    /// Type of Snappy frame.
    /// </summary>
    public enum SnappyFrameType : byte
    {
        /// <summary>
        /// Supported compressed frame containing Snappy compressed data.
        /// </summary>
        Compressed = 0,
        /// <summary>
        /// Supported uncompressed frame containing plain data.
        /// </summary>
        Uncompressed = 1,
        /// <summary>
        /// Beginning of the range of unsupported frame types reserved for future use. Exception is thrown if any such frame type is encountered.
        /// </summary>
        UnskippableFirst = 2,
        /// <summary>
        /// End of the range of unsupported frame types reserved for future use. Exception is thrown if any such frame type is encountered.
        /// </summary>
        UnskippableLast = 0x7f,
        /// <summary>
        /// Beginning of the range of unsupported frame types that are safe to skip. If encountered on input, data in these frames is skipped.
        /// </summary>
        SkippableFirst = 0x80,
        /// <summary>
        /// End of the range of unsupported frame types that are safe to skip. If encountered on input, data in these frames is skipped.
        /// </summary>
        SkippableLast = 0xfd,
        /// <summary>
        /// Padding frame. Data in this frame is ignored. Padding frames can be used for alignment purposes.
        /// </summary>
        Padding = 0xfe,
        /// <summary>
        /// Stream identifier frame. This frame contains text "sNaPpY".
        /// </summary>
        StreamIdentifier = 0xff
    }
}
