using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace O3DParse
{
    public class IniFileReader : IDisposable
    {
        private readonly Stream stream;
        private readonly bool leaveOpen;
        private readonly byte[] byteBuffer;
        private char[] charBuffer;
        private Encoding encoding;
        private Decoder decoder;
        private int position = 0;
        private int charBuffLength = 0;
        private bool detectedEncoding = false;

        private const int defaultReadSize = 4096;
        //private const int defaultReadSize = 10;

        public IniFileReader(Stream stream, bool leaveOpen = true, Encoding? encoding = null)
        {
            this.stream = stream;
            this.leaveOpen = leaveOpen;
            this.encoding = encoding ?? Encoding.Default;
            detectedEncoding = encoding != null;
            decoder = this.encoding.GetDecoder();

            byteBuffer = new byte[defaultReadSize];
            charBuffer = new char[this.encoding.GetMaxCharCount(byteBuffer.Length)];
        }

        public void Dispose()
        {
            if (!leaveOpen)
                stream.Dispose();
        }

        private enum LineEnding
        {
            None,
            UnixCR,
            MacLF,
            WindowsCRLF
        }

        /// <summary>
        /// Reads a line of characters into the given span (not including the line break).
        /// </summary>
        /// <param name="dst">The span to read the line into.</param>
        /// <returns>The number of characters read; if this is greater than the length of the 
        /// input span, then the full line couldn't be fit into the span. Returns -1 if the end of the stream was reached.</returns>
        public int ReadLine(Span<char> dst)
        {
#if DEBUG
            if (dst.Length == 0)
                throw new ArgumentException("Attempted to read line into a 0-length span!");
#endif

            //var buff = charBuffer.AsMemory()[position..];
            var buffSpan = charBuffer.AsSpan()[position..charBuffLength];

            FindLineBreak(buffSpan, out int len, out LineEnding lineEnding);

            // Fast path
            int read = 0;
            int written = 0;
            if (lineEnding != LineEnding.None)
            {
                int crLen = (lineEnding == LineEnding.WindowsCRLF ? 2 : 1);
                read = len - crLen;
                if (read <= dst.Length)
                {
                    buffSpan[..read].CopyTo(dst);
                    position += len;
                }
                else
                {
                    buffSpan[..dst.Length].CopyTo(dst);
                    position += dst.Length;
                }
                return read;
            }

            // Copy everything to the dst span
            read = buffSpan.Length;
            if (read > 0)
            {
                written = Math.Min(read, dst.Length);
                buffSpan[..written].CopyTo(dst);
                position += written;
            }

            // Keep reading more data into dst until we find a line break or dst is full
            int dstPos = written;
            while (written < dst.Length)
            {
                if (!FillCharBuff())
                    return read == 0 ? -1 : read;

                buffSpan = charBuffer.AsSpan()[..charBuffLength];

                FindLineBreak(buffSpan, out len, out lineEnding);

                int dstSpace = dst.Length - dstPos;
                if (lineEnding != LineEnding.None)
                {
                    int crLen = (lineEnding == LineEnding.WindowsCRLF ? 2 : 1);
                    int justRead = len - crLen;
                    read += justRead;
                    if (justRead <= dstSpace)
                    {
                        buffSpan[..justRead].CopyTo(dst[dstPos..]);
                        position += len;
                    }
                    else
                    {
                        buffSpan[..dstSpace].CopyTo(dst[dstPos..]);
                        position += dstSpace;
                    }
                    return read;
                }
                else
                {
                    read += buffSpan.Length;
                    if (buffSpan.Length <= dstSpace)
                    {
                        buffSpan.CopyTo(dst[dstPos..]);
                        dstPos += buffSpan.Length;
                    }
                    else
                    {
                        // No more space...
                        buffSpan[..dstSpace].CopyTo(dst[dstPos..]);
                        return read;
                    }
                }
            }
            return read;

            // Ran out of characters...
            /*var nextBuffArr = (activeBuffer == charBufferA ? charBufferB : charBufferA);
            var nextBuff = nextBuffArr.AsMemory();
            var nextBuffSpan = nextBuff.Span;
            if (len < nextBuffSpan.Length)
            {
                // Fill in the new characters
                int read = ReadChars(nextBuffSpan[len..]);
                if (read == 0)
                {
                    // No more characters to read...
                    position = activeBuffer.Length - 1;
                    return null;
                }
                // Copy accross the unfinished line
                Array.Copy(activeBuffer, position, nextBuffArr, 0, len);

                activeBuffer = nextBuffArr;
                position = 0;

                FindLineBreak(nextBuffSpan[..read], out len, out lineEnding);
                if (lineEnding != LineEnding.None)
                {
                    position += len;
                    return nextBuff[..(len - (lineEnding == LineEnding.WindowsCRLF ? 2 : 1))];
                }
            }

            // Worst case scenario, a single line does not fit in our buffer...
            var newCharBuff = ArrayPool<char>.Shared.Rent(activeBuffer.Length * 2);
            int lastLen = len;
            Array.Copy(activeBuffer, position, newCharBuff, 0, lastLen);
            activeBuffer = newCharBuff;

            if (rentedCharBuffer != null)
            {
                ArrayPool<char>.Shared.Return(rentedCharBuffer);
                rentedCharBuffer = null;
            }
            rentedCharBuffer = activeBuffer;

            while (true)
            {
                nextBuff = rentedCharBuffer.AsMemory();
                nextBuffSpan = nextBuff.Span;
                int read = ReadChars(nextBuffSpan[lastLen..]);
                nextBuff = nextBuff[..(lastLen + read)];
                nextBuffSpan = nextBuff.Span;
                if (read == 0)
                {
                    // No more characters to read...
                    position = activeBuffer.Length - 1;
                    return null;
                }

                FindLineBreak(nextBuffSpan, out len, out lineEnding);
                if (lineEnding != LineEnding.None)
                {
                    position += len;
                    return nextBuff[..(len - (lineEnding == LineEnding.WindowsCRLF ? 2 : 1))];
                }

                // Ok try and get an even bigger buffer...
                lastLen = rentedCharBuffer.Length;
                var newCharBuffer = ArrayPool<char>.Shared.Rent(lastLen * 2);
                Array.Copy(rentedCharBuffer, newCharBuffer, lastLen);
                ArrayPool<char>.Shared.Return(rentedCharBuffer);
                rentedCharBuffer = newCharBuffer;
                activeBuffer = rentedCharBuffer;
            }*/
        }

        public int Peek()
        {
            if (position < charBuffLength)
                return charBuffer[position];

            // No more chars in the buffer, read some more...
            if (!FillCharBuff())
                return -1;

            return charBuffer[0];
        }

        private static void FindLineBreak(Span<char> buff, out int len, out LineEnding lineEnding)
        {
            // \n - UNIX  \r\n - Windows  \r - Mac
            lineEnding = LineEnding.None;
            for (len = 0; len < buff.Length; len++)
            {
                char c = buff[len];
                if (c == '\n')
                {
                    lineEnding = LineEnding.UnixCR;
                    len++;
                    break;
                }
                if (c == '\r')
                {
                    if (len + 1 < buff.Length)
                    {
                        char c1 = buff[len + 1];
                        if (c1 == '\n')
                        {
                            lineEnding = LineEnding.WindowsCRLF;
                            len++;
                        }
                        else
                        {
                            lineEnding = LineEnding.MacLF;
                        }
                        len++;
                        break;
                    }
                }
                if (c == '\0')
                    break;
            }
        }

        /// <summary>
        /// Fills the char buffer with new characters resetting the charBuffLength and position variables.
        /// </summary>
        /// <returns>false if no bytes could be read from the stream.</returns>
        private bool FillCharBuff()
        {
            charBuffLength = 0;
            position = 0;
            int bytesRead = stream.Read(byteBuffer);
            if (bytesRead == 0)
                return false; // No more bytes to read

            var bytesSpan = byteBuffer.AsSpan()[..bytesRead];
            if (!detectedEncoding)
            {
                int preamble = DetectEncoding(bytesRead);
                bytesSpan = bytesSpan[preamble..];
            }

            charBuffLength = decoder.GetChars(bytesSpan, charBuffer, false);

            return true;
        }

        // Derived from C# reference source StreamReader.cs, Copyright Microsoft
        // Returns the number of preamble bytes to trim
        private int DetectEncoding(int bytesRead)
        {
            if (bytesRead < 2)
                return 0;

            int preamble = 0;
            bool changedEncoding = false;
            ushort firstTwoBytes = BinaryPrimitives.ReadUInt16LittleEndian(byteBuffer);
            if (firstTwoBytes == 0xFFFE)
            {
                // Big Endian Unicode
                encoding = Encoding.BigEndianUnicode;
                changedEncoding = true;
                preamble = 2;
            }
            else if (firstTwoBytes == 0xFEFF)
            {
                // Little Endian Unicode, or possibly little endian UTF32
                if (bytesRead < 4 || byteBuffer[2] != 0 || byteBuffer[3] != 0)
                {
                    encoding = Encoding.Unicode;
                    changedEncoding = true;
                    preamble = 2;
                }
                else
                {
                    encoding = Encoding.UTF32;
                    changedEncoding = true;
                    preamble = 4;
                }
            }
            else if (bytesRead >= 4 && firstTwoBytes == 0 && byteBuffer[2] == 0xFE && byteBuffer[3] == 0xFF)
            {
                // Big Endian UTF32
                encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
                changedEncoding = true;
                preamble = 4;
            }
            else if (bytesRead == 2)
            {
                detectedEncoding = false;
            }

            if (changedEncoding)
            {
                decoder = encoding.GetDecoder();
                int newMaxCharsPerBuffer = encoding.GetMaxCharCount(byteBuffer.Length);
                if (newMaxCharsPerBuffer > charBuffer.Length)
                    charBuffer = new char[newMaxCharsPerBuffer];
            }

            detectedEncoding = true;
            return preamble;
        }
    }
}
