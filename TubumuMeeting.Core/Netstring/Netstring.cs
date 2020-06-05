using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TubumuMeeting.Core.Netstring
{
    public class Netstring : IEnumerator<byte[]>, IEnumerable<byte[]>
    {
        private readonly byte[] _buffer;
        private byte[] _current;
        private int _offset;

        public Netstring(byte[] buffer, int offset = 0)
        {
            _buffer = buffer;
            _offset = offset;
        }

        #region IEnumerator, IEnumerable

        public bool MoveNext()
        {
            if (_offset > _buffer.Length - 3)
            {
                return false;
            }

            _current = FetchPayload();

            return true;
        }

        private byte[] FetchPayload()
        {
            var payloadLength = FetchPayloadLength(_buffer, _offset);
            if (payloadLength < 0)
            {
                throw new InvalidDataException("Illegal size field");
            }

            var netstringLength = ComputeNetstringLength(payloadLength);

            // We don't have the entire buffer yet
            if (_buffer.Length - _offset - netstringLength < 0)
            {
                throw new InvalidDataException("Don't have the entire buffer yet");
            }

            var start = _offset + (netstringLength - payloadLength - 1);
            var payload = new byte[payloadLength];
            Array.Copy(_buffer, start, payload, 0, payloadLength);
            _offset += netstringLength;
            return payload;
        }

        public byte[] Current => _current;

        object IEnumerator.Current => _current;

        public void Dispose()
        {

        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public IEnumerator<byte[]> GetEnumerator()
        {
            return this;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this;
        }

        #endregion

        private static int FetchPayloadLength(byte[] netstring, int offset)
        {
            var len = 0;
            var i = 0;
            for (i = offset; i < netstring.Length; i++)
            {
                var cc = netstring[i];

                if (cc == ':'/*0x3a*/)
                {
                    if (i == offset)
                    {
                        throw new Exception("Invalid netstring with leading ':'");
                    }

                    return len;
                }

                if (cc < '0'/*0x30*/ || cc > '9'/*0x39*/)
                {
                    throw new Exception($"Unexpected character ${cc} found at offset ");
                }

                if (len == 0 && i > offset)
                {
                    throw new Exception("Invalid netstring with leading 0");
                }

                len = len * 10 + cc - '0'/*0x30*/;
            }

            // We didn't get a complete length specification
            if (i == netstring.Length)
            {
                return -1;
            }

            return len;
        }

        private static int ComputeNetstringLength(int payloadLength)
        {
            // Negative values are special (see nsPayloadLength()); just return it
            if (payloadLength < 0)
            {
                return payloadLength;
            }

            // Compute the number of digits in the length specifier. Stop at
            // any value < 10 and just add 1 later (this catches the case where
            // '0' requires a digit.
            var nslen = payloadLength;
            while (payloadLength >= 10)
            {
                nslen += 1;
                payloadLength /= 10;
            }

            // nslen + 1 (last digit) + 1 (:) + 1 (,)
            return nslen + 3;
        }
    }
}
