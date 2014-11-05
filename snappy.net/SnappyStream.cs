using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
#if SNAPPY_ASYNC
using System.Threading.Tasks;
#endif

namespace Snappy
{
    /// <summary>
    /// Compression stream similar to GZipStream except this one uses Snappy compression.
    /// This stream uses standard Snappy framing format that supports streams of unbounded size
    /// and includes CRC checksums of all transmitted data.
    /// This stream can operate in one of two modes: compression or decompression.
    /// When compressing, use Write* methods. When decompressing, use Read* methods.
    /// If SnappyStream is opened for compression and immediately closed, the resulting stream
    /// will be a valid Snappy stream containing zero bytes of uncompressed data.
    /// </summary>
    public class SnappyStream : Stream
    {
        Stream Stream;
        readonly CompressionMode Mode;
        readonly bool LeaveOpen;
        SnappyFrame Frame = new SnappyFrame();
        byte[] Buffer = new byte[256];
        int BufferUsage;
        int BufferRead;
        bool InitializedStream;
        bool BadStream;

        /// <summary>
        /// Gets a value indicating whether the current stream supports reading. True for decompression stream.
        /// </summary>
        public override bool CanRead { get { return Stream != null && Mode == CompressionMode.Decompress && Stream.CanRead; } }
        /// <summary>
        /// Gets a value indicating whether the current stream supports writing. True for compression stream.
        /// </summary>
        public override bool CanWrite { get { return Stream != null && Mode == CompressionMode.Compress && Stream.CanWrite; } }
        /// <summary>
        /// Gets a value indicating whether the current stream supports seeking. Always false for SnappyStream.
        /// </summary>
        public override bool CanSeek { get { return false; } }
        /// <summary>
        /// Gets the length in bytes of the stream. Not supported in SnappyStream.
        /// </summary>
        public override long Length { get { throw new NotSupportedException(); } }
        /// <summary>
        /// Gets or sets the position within the current stream. Not supported in SnappyStream.
        /// </summary>
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }

        /// <summary>
        /// Creates new SnappyStream using specified mode of operation.
        /// </summary>
        /// <param name="stream">Underlying stream holding compressed data. It is automatically closed when SnappyStream is closed.</param>
        /// <param name="mode">
        /// Use mode Compress if SnappyStream is used to compress data and write it to the underlying stream.
        /// Use mode Decompress if SnappyStream is used to decompress data that is retrieved in compressed form from the underlying stream.
        /// </param>
        public SnappyStream(Stream stream, CompressionMode mode) : this(stream, mode, false) { }

        /// <summary>
        /// Creates new SnappyStream using specified mode of operation with an option to leave the underlying stream open.
        /// </summary>
        /// <param name="stream">Underlying stream holding compressed data.</param>
        /// <param name="mode">
        /// Use mode Compress if SnappyStream is used to compress data and write it to the underlying stream.
        /// Use mode Decompress if SnappyStream is used to decompress data that is retrieved in compressed form from the underlying stream.
        /// </param>
        /// <param name="leaveOpen">False to close the underlying stream when SnappyStream is closed. True to leave the underlying stream open.</param>
        public SnappyStream(Stream stream, CompressionMode mode, bool leaveOpen)
        {
            Stream = stream;
            Mode = mode;
            LeaveOpen = leaveOpen;
        }

        /// <summary>
        /// Dispose the stream. Remaining data is flushed and underlying stream is closed.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources. False to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (Mode == CompressionMode.Compress && disposing)
                    Flush();
            }
            finally
            {
                try
                {
                    if (!LeaveOpen && disposing)
                        Stream.Close();
                }
                finally
                {
                    Stream = null;
                }
            }
        }

        /// <summary>
        /// Reads uncompressed data from underlying compressed stream.
        /// </summary>
        /// <param name="buffer">Output buffer where uncompressed data will be written.</param>
        /// <param name="offset">Offset into the output buffer where uncompressed data will be written.</param>
        /// <param name="count">Maximum size of uncompressed data to read.</param>
        /// <returns>
        /// Amount of data actually stored in the output buffer.
        /// This might be less than the count parameter if end of stream is encountered.
        /// Return value is zero if there is no more data in the stream.
        /// </returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                EnsureDecompressionMode();
                ValidateRange(buffer, offset, count);
                InitializeStream();
                int total = 0;
                while (count > 0)
                {
                    if (!EnsureAvailable())
                        return total;
                    int append = Math.Min(count, BufferUsage - BufferRead);
                    Array.Copy(Buffer, BufferRead, buffer, offset, append);
                    total += append;
                    offset += append;
                    count -= append;
                    BufferRead += append;
                }
                return total;
            }
            catch
            {
                BadStream = true;
                throw;
            }
        }

#if SNAPPY_ASYNC
        /// <summary>
        /// Reads uncompressed data from underlying compressed stream.
        /// </summary>
        /// <param name="buffer">Output buffer where uncompressed data will be written.</param>
        /// <param name="offset">Offset into the output buffer where uncompressed data will be written.</param>
        /// <param name="count">Maximum size of uncompressed data to read.</param>
        /// <param name="cancellation">Cancellation token that can be used to cancel the read operation.</param>
        /// <returns>
        /// Amount of data actually stored in the output buffer.
        /// This might be less than the count parameter if end of stream is encountered.
        /// Return value is zero if there is no more data in the stream.
        /// </returns>
        public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellation)
        {
            try
            {
                EnsureDecompressionMode();
                ValidateRange(buffer, offset, count);
                await InitializeStreamAsync(cancellation);
                int total = 0;
                while (count > 0)
                {
                    if (!await EnsureAvailableAsync(cancellation))
                        return total;
                    int append = Math.Min(count, BufferUsage - BufferRead);
                    Array.Copy(Buffer, BufferRead, buffer, offset, append);
                    total += append;
                    offset += append;
                    count -= append;
                    BufferRead += append;
                }
                return total;
            }
            catch
            {
                BadStream = true;
                throw;
            }
        }
#endif

        /// <summary>
        /// Reads single byte from the underlying stream.
        /// </summary>
        /// <returns>Byte read from the stream or -1 if end of stream has been reached.</returns>
        public override int ReadByte()
        {
            try
            {
                EnsureDecompressionMode();
                InitializeStream();
                if (!EnsureAvailable())
                    return -1;
                byte result = Buffer[BufferRead];
                ++BufferRead;
                return result;
            }
            catch
            {
                BadStream = true;
                throw;
            }
        }

        /// <summary>
        /// Compresses given uncompressed data and writes the compressed data to the underlying stream.
        /// This method will buffer some of the data in order to compress 64KB at a time.
        /// Use Flush method to write the data to the underlying stream immediately.
        /// </summary>
        /// <param name="buffer">Input buffer containing uncompressed data to be compressed and written to the underlying stream.</param>
        /// <param name="offset">Offset into the input buffer where uncompressed data is located.</param>
        /// <param name="count">Length of the uncompressed data in the input buffer. Zero-length data has no effect on the stream.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            try
            {
                EnsureCompressionMode();
                ValidateRange(buffer, offset, count);
                InitializeStream();
                while (count > 0)
                {
                    int append = Math.Min(count, SnappyFrame.MaxFrameSize - BufferUsage);
                    EnsureBuffer(BufferUsage + append);
                    Array.Copy(buffer, offset, Buffer, BufferUsage, append);
                    offset += append;
                    count -= append;
                    BufferUsage += append;
                    if (BufferUsage == SnappyFrame.MaxFrameSize)
                        Flush();
                }
            }
            catch
            {
                BadStream = true;
                throw;
            }
        }

#if SNAPPY_ASYNC
        /// <summary>
        /// Compresses given uncompressed data and writes the compressed data to the underlying stream.
        /// This method will buffer some of the data in order to compress 64KB at a time.
        /// Use FlushAsync method to write the data to the underlying stream immediately.
        /// </summary>
        /// <param name="buffer">Input buffer containing uncompressed data to be compressed and written to the underlying stream.</param>
        /// <param name="offset">Offset into the input buffer where uncompressed data is located.</param>
        /// <param name="count">Length of the uncompressed data in the input buffer. Zero-length data has no effect on the stream.</param>
        /// <param name="cancellation">Cancellation token that can be used to cancel the write operation.</param>
        /// <returns>Task object indicating completion of the write.</returns>
        public async override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellation)
        {
            try
            {
                EnsureCompressionMode();
                ValidateRange(buffer, offset, count);
                await InitializeStreamAsync(cancellation);
                while (count > 0)
                {
                    int append = Math.Min(count, SnappyFrame.MaxFrameSize - BufferUsage);
                    EnsureBuffer(BufferUsage + append);
                    Array.Copy(buffer, offset, Buffer, BufferUsage, append);
                    offset += append;
                    count -= append;
                    BufferUsage += append;
                    if (BufferUsage == SnappyFrame.MaxFrameSize)
                        await FlushAsync(cancellation);
                }
            }
            catch
            {
                BadStream = true;
                throw;
            }
        }
#endif

        /// <summary>
        /// Writes single byte of uncompressed data to the stream and queues it for compression.
        /// This method will buffer data in order to compress 64KB at a time.
        /// Use Flush method to write the data to the underlying stream immediately.
        /// </summary>
        /// <param name="value">Byte of uncompressed data to be added to the stream.</param>
        public override void WriteByte(byte value)
        {
            try
            {
                EnsureCompressionMode();
                InitializeStream();
                EnsureBuffer(BufferUsage + 1);
                Buffer[BufferUsage] = value;
                ++BufferUsage;
                if (BufferUsage == SnappyFrame.MaxFrameSize)
                    Flush();
            }
            catch
            {
                BadStream = true;
                throw;
            }
        }

        /// <summary>
        /// Flushes all data buffered by previous calls to Write* methods.
        /// Remaining data is compressed and written to the underlying stream.
        /// </summary>
        public override void Flush()
        {
            try
            {
                EnsureCompressionMode();
                InitializeStream();
                if (BufferUsage > 0)
                {
                    Frame.SetCompressed(Buffer, 0, BufferUsage);
                    BufferUsage = 0;
                    Frame.Write(Stream);
                }
            }
            catch
            {
                BadStream = true;
                throw;
            }
        }

#if SNAPPY_ASYNC
        /// <summary>
        /// Flushes all data buffered by previous calls to Write* methods.
        /// Remaining data is compressed and written to the underlying stream.
        /// </summary>
        /// <param name="cancellation">Cancellation token that can be used to cancel the flush operation.</param>
        /// <returns>Task object indicating completion of the flush.</returns>
        public async override Task FlushAsync(CancellationToken cancellation)
        {
            try
            {
                EnsureCompressionMode();
                await InitializeStreamAsync(cancellation);
                if (BufferUsage > 0)
                {
                    Frame.SetCompressed(Buffer, 0, BufferUsage);
                    BufferUsage = 0;
                    await Frame.WriteAsync(Stream, cancellation);
                }
            }
            catch
            {
                BadStream = true;
                throw;
            }
        }
#endif

        /// <summary>
        /// Sets the length of the current stream. Not supported in SnappyStream.
        /// </summary>
        /// <param name="value">The desired length of the current stream in bytes.</param>
        public override void SetLength(long value) { throw new NotSupportedException(); }

        /// <summary>
        /// Sets the position within the current stream. Not supported in SnappyStream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter.</param>
        /// <param name="origin">A value of type SeekOrigin indicating the reference point used to obtain the new position.</param>
        /// <returns></returns>
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }

        void InitializeStream()
        {
            if (!InitializedStream)
            {
                if (Mode == CompressionMode.Compress)
                {
                    Frame.SetStreamIdentifier();
                    Frame.Write(Stream);
                }
                else
                {
                    if (!Frame.Read(Stream))
                        throw new EndOfStreamException();
                    if (Frame.Type != SnappyFrameType.StreamIdentifier)
                        throw new ArgumentOutOfRangeException();
                }
                InitializedStream = true;
            }
        }

#if SNAPPY_ASYNC
        async Task InitializeStreamAsync(CancellationToken cancellation)
        {
            if (!InitializedStream)
            {
                if (Mode == CompressionMode.Compress)
                {
                    Frame.SetStreamIdentifier();
                    await Frame.WriteAsync(Stream, cancellation);
                }
                else
                {
                    if (!await Frame.ReadAsync(Stream, cancellation))
                        throw new EndOfStreamException();
                    if (Frame.Type != SnappyFrameType.StreamIdentifier)
                        throw new ArgumentOutOfRangeException();
                }
                InitializedStream = true;
            }
        }
#endif

        bool EnsureAvailable()
        {
            if (BufferRead >= BufferUsage)
            {
                do
                {
                    if (!Frame.Read(Stream))
                        return false;
                } while (Frame.Type != SnappyFrameType.Compressed && Frame.Type != SnappyFrameType.Uncompressed || Frame.DataLength == 0);
                EnsureBuffer(Frame.DataLength);
                BufferRead = 0;
                BufferUsage = Frame.DataLength;
                Frame.GetData(Buffer);
            }
            return true;
        }

#if SNAPPY_ASYNC
        async Task<bool> EnsureAvailableAsync(CancellationToken cancellation)
        {
            if (BufferRead >= BufferUsage)
            {
                do
                {
                    if (!await Frame.ReadAsync(Stream, cancellation))
                        return false;
                } while (Frame.Type != SnappyFrameType.Compressed && Frame.Type != SnappyFrameType.Uncompressed || Frame.DataLength == 0);
                EnsureBuffer(Frame.DataLength);
                BufferRead = 0;
                BufferUsage = Frame.DataLength;
                Frame.GetData(Buffer);
            }
            return true;
        }
#endif

        void EnsureCompressionMode()
        {
            CheckStream();
            if (Mode != CompressionMode.Compress)
                throw new InvalidOperationException("Use read operations on decompression stream");
        }

        void EnsureDecompressionMode()
        {
            CheckStream();
            if (Mode != CompressionMode.Decompress)
                throw new InvalidOperationException("Use write operations on compression stream");
        }

        void CheckStream()
        {
            if (Stream == null)
                throw new ObjectDisposedException("SnappyStream");
            if (BadStream)
                throw new InvalidOperationException("SnappyStream is in broken state due to previous error");
        }

        void ValidateRange(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException();
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();
        }

        void EnsureBuffer(int size)
        {
            if (size > Buffer.Length)
            {
                var newSize = 2 * Buffer.Length;
                while (newSize < size)
                    newSize *= 2;
                var newBuffer = new byte[newSize];
                Array.Copy(Buffer, 0, newBuffer, 0, BufferUsage);
                Buffer = newBuffer;
            }
        }
    }
}
