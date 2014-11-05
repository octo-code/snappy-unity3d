using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
#if SNAPPY_ASYNC
using System.Threading.Tasks;
#endif

namespace Snappy
{
    /// <summary>
    /// Represents single Snappy frame that conforms to Snappy framing format.
    /// </summary>
    public class SnappyFrame
    {
        /// <summary>
        /// Maximum size of uncompressed data in Snappy frame. It's 64KB in current version of the format.
        /// </summary>
        public const int MaxFrameSize = 1 << 16;
        /// <summary>
        /// Stream identifier text. It is found in ASCII in identifier frame that leads the stream of Snappy frames.
        /// </summary>
        public readonly byte[] StreamIdentifier = Encoding.ASCII.GetBytes("sNaPpY");
        static readonly int MaxBufferUsage = SnappyCodec.GetMaxCompressedLength(MaxFrameSize);
        byte[] WordBuffer = new byte[4];
        byte[] Buffer;
        int BufferUsage;

        /// <summary>
        /// Type of Snappy frame. It contains decoded frame type upon deserialization. It is set automatically by Set* methods.
        /// </summary>
        public SnappyFrameType Type { get; private set; }
        /// <summary>
        /// Data checksum is present in compressed and uncompressed frames. Otherwise it is zero.
        /// It is computed automatically by Set* methods.
        /// It is automatically checked when reading data through Data property or GetData method.
        /// It is CRC-32C (Castagnoli) of the uncompressed data with final CRC obfuscated
        /// with transformation ((crc &gt;&gt; 15) | (crc &lt;&lt; 17)) + 0xa282ead8.
        /// </summary>
        public uint Checksum { get; private set; }
        /// <summary>
        /// Length of uncompressed data for data frames. Querying this property takes O(1) time.
        /// For compressed and uncompressed data, data length is never larger than 64KB.
        /// For padding frames, this property is equal to the length of padding data.
        /// Identification frame has data length of 6.
        /// Data length is automatically set by Set* methods and upon frame deserialization.
        /// </summary>
        public int DataLength { get; private set; }
        /// <summary>
        /// Uncompressed data in the frame. It is available only for frame type Compressed or Uncompressed.
        /// Querying this property triggers CRC check of the data, which might cause exceptions.
        /// Returned data is never larger than 64KB.
        /// </summary>
        public byte[] Data
        {
            get
            {
                if (Type != SnappyFrameType.Compressed && Type != SnappyFrameType.Uncompressed)
                    throw new InvalidOperationException();
                var result = new byte[DataLength];
                GetData(result);
                return result;
            }
        }

        /// <summary>
        /// Create new Snappy frame. Frame is initialized to zero-length padding frame by default.
        /// </summary>
        public SnappyFrame()
        {
            SetPadding(0);
        }

        /// <summary>
        /// Resets this frame to stream identifier frame. First frame in the stream must be identifier frame.
        /// </summary>
        public void SetStreamIdentifier()
        {
            BufferUsage = 0;
            Checksum = 0;
            DataLength = 6;
            Type = SnappyFrameType.StreamIdentifier;
        }

        /// <summary>
        /// Resets this frame to padding frame of specified size.
        /// </summary>
        /// <param name="size">Size of padding data excluding the 4 bytes of frame header. Maximum padding size is 16MB.</param>
        public void SetPadding(int size)
        {
            if (size < 0 || size >= (1 << 24))
                throw new ArgumentOutOfRangeException();
            BufferUsage = 0;
            Checksum = 0;
            DataLength = size;
            Type = SnappyFrameType.Padding;
        }

        /// <summary>
        /// Resets this frame to contain compressed data.
        /// </summary>
        /// <param name="data">Uncompressed data that is compressed by this method before being stored in the frame. Maximum data size is 64KB.</param>
        public void SetCompressed(byte[] data)
        {
            SetCompressed(data, 0, data.Length);
        }

        /// <summary>
        /// Resets this frame to contain compressed data.
        /// </summary>
        /// <param name="data">Input buffer containing uncompressed data that is compressed by this method before being stored in the frame.</param>
        /// <param name="offset">Offset of uncompressed data in the input buffer.</param>
        /// <param name="count">Size of uncompressed data in the input buffer. Maximum data size is 64KB.</param>
        public void SetCompressed(byte[] data, int offset, int count)
        {
            CheckRange(data, offset, count);
            CheckMaxFrameSize(count);
            EnsureBuffer(SnappyCodec.GetMaxCompressedLength(count));
            BufferUsage = SnappyCodec.Compress(data, offset, count, Buffer, 0);
            DataLength = count;
            Checksum = ComputeMasked(data, offset, count);
            Type = SnappyFrameType.Compressed;
        }

        /// <summary>
        /// Resets this frame to contain uncompressed data.
        /// </summary>
        /// <param name="data">Uncompressed data to store in the frame. Maximum data size is 64KB.</param>
        public void SetUncompressed(byte[] data)
        {
            SetUncompressed(data, 0, data.Length);
        }

        /// <summary>
        /// Resets this frame to contain uncompressed data.
        /// </summary>
        /// <param name="data">Input buffer containing uncompressed data to be stored in this frame.</param>
        /// <param name="offset">Offset of uncompressed data in the input buffer.</param>
        /// <param name="count">Size of uncompressed data in the input buffer. Maximum data size is 64KB.</param>
        public void SetUncompressed(byte[] data, int offset, int count)
        {
            CheckRange(data, offset, count);
            CheckMaxFrameSize(count);
            EnsureBuffer(count);
            Array.Copy(data, offset, Buffer, 0, count);
            BufferUsage = count;
            DataLength = count;
            Checksum = ComputeMasked(data, offset, count);
            Type = SnappyFrameType.Uncompressed;
        }

        /// <summary>
        /// Retrieves data from the frame. Data is uncompressed before being stored in the output buffer.
        /// CRC of the data is checked and CRC failure results in exception.
        /// </summary>
        /// <param name="buffer">Output buffer where uncompressed data is stored. It must be at least DataLength bytes long.</param>
        public void GetData(byte[] buffer)
        {
            GetData(buffer, 0);
        }

        /// <summary>
        /// Retrieves data from the frame. Data is uncompressed before being stored in the output buffer.
        /// CRC of the data is checked and CRC failure results in exception.
        /// </summary>
        /// <param name="buffer">Output buffer where uncompressed data is stored. Buffer length minus offset must be at least DataLength bytes.</param>
        /// <param name="offset">Offset into the output buffer where uncompressed data will be stored.</param>
        public void GetData(byte[] buffer, int offset)
        {
            if (Type == SnappyFrameType.Compressed)
            {
                var count = SnappyCodec.Uncompress(Buffer, 0, BufferUsage, buffer, offset);
                if (ComputeMasked(buffer, offset, count) != Checksum)
                    throw new InvalidDataException();
            }
            else if (Type == SnappyFrameType.Uncompressed)
            {
                if (ComputeMasked(Buffer, offset, BufferUsage) != Checksum)
                    throw new InvalidDataException();
                Array.Copy(Buffer, 0, buffer, offset, BufferUsage);
            }
            else
                throw new InvalidOperationException();
        }

        /// <summary>
        /// Retrieves Snappy frame from underlying stream. Retrieved frame data is stored in properties of this object.
        /// Return value indicates end of stream. Exceptions indicate data integrity errors and underlying stream errors.
        /// </summary>
        /// <param name="stream">Underlying stream that will be read by this method.</param>
        /// <returns>
        /// True if frame was successfully retrieved. False if there are no more frames in the stream, i.e. the end of stream has been reached.
        /// Note that reaching the end of stream in the middle of the frame is considered an error and causes exception instead.
        /// </returns>
        public bool Read(Stream stream)
        {
            try
            {
                var headerRead = stream.Read(WordBuffer, 0, 4);
                if (headerRead == 0)
                    return false;
                EnsureRead(stream, WordBuffer, headerRead, 4 - headerRead);
                Type = (SnappyFrameType)WordBuffer[0];
                int length = WordBuffer[1] + ((int)WordBuffer[2] << 8) + ((int)WordBuffer[3] << 16);
                if (Type == SnappyFrameType.Compressed || Type == SnappyFrameType.Uncompressed)
                {
                    if (length < 4)
                        throw new InvalidDataException();
                    EnsureRead(stream, WordBuffer, 0, 4);
                    Checksum = (uint)WordBuffer[0] + ((uint)WordBuffer[1] << 8) + ((uint)WordBuffer[2] << 16) + ((uint)WordBuffer[3] << 24);
                    BufferUsage = length - 4;
                    if (BufferUsage > MaxBufferUsage)
                        throw new InvalidDataException();
                    EnsureBuffer(BufferUsage);
                    EnsureRead(stream, Buffer, 0, BufferUsage);
                    DataLength = Type == SnappyFrameType.Uncompressed ? BufferUsage : SnappyCodec.GetUncompressedLength(Buffer, 0, BufferUsage);
                    if (DataLength > MaxFrameSize)
                        throw new InvalidDataException();
                }
                else if (Type == SnappyFrameType.Padding || (byte)Type >= (byte)SnappyFrameType.SkippableFirst && (byte)Type <= (byte)SnappyFrameType.SkippableLast)
                {
                    DataLength = length;
                    BufferUsage = 0;
                    Checksum = 0;
                    SkipBytes(stream, length);
                }
                else if (Type == SnappyFrameType.StreamIdentifier)
                {
                    if (length != 6)
                        throw new InvalidOperationException();
                    DataLength = 6;
                    BufferUsage = 0;
                    Checksum = 0;
                    EnsureBuffer(6);
                    EnsureRead(stream, Buffer, 0, 6);
                    if (!Utils.BuffersEqual(Buffer, StreamIdentifier, 6))
                        throw new InvalidDataException();
                }
                else
                    throw new InvalidDataException();
                return true;
            }
            catch
            {
                SetPadding(0);
                throw;
            }
        }

#if SNAPPY_ASYNC
        /// <summary>
        /// Retrieves Snappy frame from underlying stream. Retrieved frame data is stored in properties of this object.
        /// Return value indicates end of stream. Exceptions indicate data integrity errors and underlying stream errors.
        /// </summary>
        /// <param name="stream">Underlying stream that will be read by this method.</param>
        /// <returns>
        /// True if frame was successfully retrieved. False if there are no more frames in the stream, i.e. the end of stream has been reached.
        /// Note that reaching the end of stream in the middle of the frame is considered an error and causes exception instead.
        /// </returns>
        public Task<bool> ReadAsync(Stream stream) { return ReadAsync(stream, CancellationToken.None); }

        /// <summary>
        /// Retrieves Snappy frame from underlying stream. Retrieved frame data is stored in properties of this object.
        /// Return value indicates end of stream. Exceptions indicate data integrity errors and underlying stream errors.
        /// </summary>
        /// <param name="stream">Underlying stream that will be read by this method.</param>
        /// <param name="cancellation">Cancellation token that can be used to cancel the read operation.</param>
        /// <returns>
        /// True if frame was successfully retrieved. False if there are no more frames in the stream, i.e. the end of stream has been reached.
        /// Note that reaching the end of stream in the middle of the frame is considered an error and causes exception instead.
        /// </returns>
        public async Task<bool> ReadAsync(Stream stream, CancellationToken cancellation)
        {
            try
            {
                var headerRead = await stream.ReadAsync(WordBuffer, 0, 4, cancellation);
                if (headerRead == 0)
                    return false;
                await EnsureReadAsync(stream, WordBuffer, headerRead, 4 - headerRead, cancellation);
                Type = (SnappyFrameType)WordBuffer[0];
                int length = WordBuffer[1] + ((int)WordBuffer[2] << 8) + ((int)WordBuffer[3] << 16);
                if (Type == SnappyFrameType.Compressed || Type == SnappyFrameType.Uncompressed)
                {
                    if (length < 4)
                        throw new InvalidDataException();
                    await EnsureReadAsync(stream, WordBuffer, 0, 4, cancellation);
                    Checksum = (uint)WordBuffer[0] + ((uint)WordBuffer[1] << 8) + ((uint)WordBuffer[2] << 16) + ((uint)WordBuffer[3] << 24);
                    BufferUsage = length - 4;
                    if (BufferUsage > MaxBufferUsage)
                        throw new InvalidDataException();
                    EnsureBuffer(BufferUsage);
                    await EnsureReadAsync(stream, Buffer, 0, BufferUsage, cancellation);
                    DataLength = Type == SnappyFrameType.Uncompressed ? BufferUsage : SnappyCodec.GetUncompressedLength(Buffer, 0, BufferUsage);
                    if (DataLength > MaxFrameSize)
                        throw new InvalidDataException();
                }
                else if (Type == SnappyFrameType.Padding || (byte)Type >= (byte)SnappyFrameType.SkippableFirst && (byte)Type <= (byte)SnappyFrameType.SkippableLast)
                {
                    DataLength = length;
                    BufferUsage = 0;
                    Checksum = 0;
                    await SkipBytesAsync(stream, length, cancellation);
                }
                else if (Type == SnappyFrameType.StreamIdentifier)
                {
                    if (length != 6)
                        throw new InvalidOperationException();
                    DataLength = 6;
                    BufferUsage = 0;
                    Checksum = 0;
                    EnsureBuffer(6);
                    await EnsureReadAsync(stream, Buffer, 0, 6, cancellation);
                    if (!Utils.BuffersEqual(Buffer, StreamIdentifier, 6))
                        throw new InvalidDataException();
                }
                else
                    throw new InvalidDataException();
                return true;
            }
            catch
            {
                SetPadding(0);
                throw;
            }
        }
#endif

        /// <summary>
        /// Writes the frame into the underlying stream.
        /// </summary>
        /// <param name="stream">Underlying stream where the frame will be written.</param>
        public void Write(Stream stream)
        {
            if (Type != SnappyFrameType.Compressed && Type != SnappyFrameType.Uncompressed && Type != SnappyFrameType.StreamIdentifier && Type != SnappyFrameType.Padding)
                throw new InvalidOperationException();
            
            int totalLength = Type == SnappyFrameType.Compressed || Type == SnappyFrameType.Uncompressed ? BufferUsage + 4 : DataLength;
            
            WordBuffer[0] = (byte)Type;
            WordBuffer[1] = (byte)totalLength;
            WordBuffer[2] = (byte)(totalLength >> 8);
            WordBuffer[3] = (byte)(totalLength >> 16);
            stream.Write(WordBuffer, 0, 4);
            
            if (Type == SnappyFrameType.Compressed || Type == SnappyFrameType.Uncompressed)
            {
                WordBuffer[0] = (byte)Checksum;
                WordBuffer[1] = (byte)(Checksum >> 8);
                WordBuffer[2] = (byte)(Checksum >> 16);
                WordBuffer[3] = (byte)(Checksum >> 24);
                stream.Write(WordBuffer, 0, 4);
            }

            if (Type == SnappyFrameType.StreamIdentifier)
                stream.Write(StreamIdentifier, 0, 6);
            else if (Type == SnappyFrameType.Padding)
                WriteZeroes(stream, DataLength);
            else
                stream.Write(Buffer, 0, BufferUsage);
        }

#if SNAPPY_ASYNC
        /// <summary>
        /// Writes the frame into the underlying stream.
        /// </summary>
        /// <param name="stream">Underlying stream where the frame will be written.</param>
        /// <returns>Task object indicating completion of the write.</returns>
        public Task WriteAsync(Stream stream) { return WriteAsync(stream, CancellationToken.None); }

        /// <summary>
        /// Writes the frame into the underlying stream.
        /// </summary>
        /// <param name="stream">Underlying stream where the frame will be written.</param>
        /// <param name="cancellation">Cancellation token that can be used to cancel the write operation.</param>
        /// <returns>Task object indicating completion of the write.</returns>
        public async Task WriteAsync(Stream stream, CancellationToken cancellation)
        {
            if (Type != SnappyFrameType.Compressed && Type != SnappyFrameType.Uncompressed && Type != SnappyFrameType.StreamIdentifier && Type != SnappyFrameType.Padding)
                throw new InvalidOperationException();

            int totalLength = Type == SnappyFrameType.Compressed || Type == SnappyFrameType.Uncompressed ? BufferUsage + 4 : DataLength;

            WordBuffer[0] = (byte)Type;
            WordBuffer[1] = (byte)totalLength;
            WordBuffer[2] = (byte)(totalLength >> 8);
            WordBuffer[3] = (byte)(totalLength >> 16);
            await stream.WriteAsync(WordBuffer, 0, 4, cancellation);

            if (Type == SnappyFrameType.Compressed || Type == SnappyFrameType.Uncompressed)
            {
                WordBuffer[0] = (byte)Checksum;
                WordBuffer[1] = (byte)(Checksum >> 8);
                WordBuffer[2] = (byte)(Checksum >> 16);
                WordBuffer[3] = (byte)(Checksum >> 24);
                await stream.WriteAsync(WordBuffer, 0, 4, cancellation);
            }

            if (Type == SnappyFrameType.StreamIdentifier)
                await stream.WriteAsync(StreamIdentifier, 0, 6, cancellation);
            else if (Type == SnappyFrameType.Padding)
                await WriteZeroesAsync(stream, DataLength, cancellation);
            else
                await stream.WriteAsync(Buffer, 0, BufferUsage, cancellation);
        }
#endif

        void EnsureRead(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
                count -= read;
            }
        }

#if SNAPPY_ASYNC
        async Task EnsureReadAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellation)
        {
            while (count > 0)
            {
                int read = await stream.ReadAsync(buffer, offset, count, cancellation);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
                count -= read;
            }
        }
#endif

        void SkipBytes(Stream stream, int count)
        {
            EnsureBuffer(Math.Min(count, MaxFrameSize));
            while (count > 0)
            {
                int read = stream.Read(Buffer, 0, Math.Min(count, Buffer.Length));
                if (read <= 0)
                    throw new EndOfStreamException();
                count -= read;
            }
        }

#if SNAPPY_ASYNC
        async Task SkipBytesAsync(Stream stream, int count, CancellationToken cancellation)
        {
            EnsureBuffer(Math.Min(count, MaxFrameSize));
            while (count > 0)
            {
                int read = await stream.ReadAsync(Buffer, 0, Math.Min(count, Buffer.Length), cancellation);
                if (read <= 0)
                    throw new EndOfStreamException();
                count -= read;
            }
        }
#endif

        void WriteZeroes(Stream stream, int count)
        {
            var reserved = Math.Min(count, MaxFrameSize);
            EnsureBuffer(reserved);
            for (int i = 0; i < reserved; ++i)
                Buffer[i] = 0;
            while (count > 0)
            {
                int written = Math.Min(count, reserved);
                stream.Write(Buffer, 0, reserved);
                count -= written;
            }
        }

#if SNAPPY_ASYNC
        async Task WriteZeroesAsync(Stream stream, int count, CancellationToken cancellation)
        {
            var reserved = Math.Min(count, MaxFrameSize);
            EnsureBuffer(reserved);
            for (int i = 0; i < reserved; ++i)
                Buffer[i] = 0;
            while (count > 0)
            {
                int written = Math.Min(count, reserved);
                await stream.WriteAsync(Buffer, 0, reserved, cancellation);
                count -= written;
            }
        }
#endif

        static uint ComputeMasked(byte[] data, int offset, int count)
        {
            byte[] dataPart = new byte[count];
            data.CopyTo(dataPart, offset);
            var checksum = Crc32.Compute(dataPart);
            return ((checksum >> 15) | (checksum << 17)) + 0xa282ead8;
        }

        void CheckRange(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException();
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();
        }

        void CheckMaxFrameSize(int size)
        {
            if (size < 0 || size > MaxFrameSize)
                throw new ArgumentOutOfRangeException();
        }

        void EnsureBuffer(int size)
        {
            if (Buffer == null || Buffer.Length < size)
                Buffer = new byte[size];
        }
    }
}
