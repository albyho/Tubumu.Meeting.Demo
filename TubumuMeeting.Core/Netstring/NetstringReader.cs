using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TubumuMeeting.Netstring
{
    public class NetstringReader : IEnumerator<string>, IEnumerable<string>, IDisposable
    {
        private static readonly Regex SizePattern = new Regex("^(?<size>[1-9]\\d*)(?<terminator>:)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Decodes a single netstring and returns its payload. For streams of netstrings use the netstring object instead of multiple calls to this method.
        /// </summary>
        /// <param name="value">The netstring to decode.</param>
        /// <exception cref="System.OverflowException" />Raised if value requests a size greater than Int32.MaxLength characters.</exception>
        /// <exception cref="System.IO.InvalidDataException">Raised if value does not strictly adhere to the netstring protocol.</exception>
        /// <returns>The value of this netstring.</returns>
        public static string Decode(string value)
        {
            var match = SizePattern.Match(value);

            if (match.Success == false || match.Groups["terminator"].Success == false)
            {
                throw new InvalidDataException("Illegal size field");
            }

            var sizeGroup = match.Groups["size"];

            var size = Convert.ToInt32(sizeGroup.Value);

            value = value.Remove(0, match.Length);

            if (value.Length == size + 1)
            {
                try
                {
                    return value.Substring(0, size);
                }
                finally
                {
                    if (value.Remove(0, size) != ",")
                    {
                        throw new InvalidDataException("Payload terminator not found");
                    }
                }
            }
            else
            {
                throw new InvalidDataException("Exceeded requested size");
            }
        }

        /// <summary>
        /// Convenience method which constructs a StringReader for value.
        /// </summary>
        /// <param name="value">A string containing one or more complete netstrings.</param>
        /// <param name="maxLength">The maximum length allowed for any one netstring.</param>
        public NetstringReader(String value, int maxLength = Int32.MaxValue) : this(new StringReader(value), maxLength)
        {
        }

        /// <summary>
        /// Constructs a new Netsrings instance which will emit netstrings found in reader.
        /// </summary>
        /// <param name="reader">A stream of netstrings.</param>
        /// /// <param name="maxLength">The maximum length allowed for any one netstring.</param>
        public NetstringReader(TextReader reader, int maxLength = Int32.MaxValue)
        {
            this.reader = reader;
            this.builder = new StringBuilder(2048, maxLength);
        }

        private int? size;
        private readonly char[] buffer = new char[2048];
        private readonly TextReader reader;
        private readonly StringBuilder builder;
        private string current;

        /// <summary>
        /// The maximum length allowed for any one netstring.
        /// </summary>
        public int MaxLength
        {
            get { return this.builder.MaxCapacity; }
        }

        /// <summary>
        /// The most recently read netstring in reader.
        /// </summary>
        public string Current
        {
            get { return this.current; }
        }

        object IEnumerator.Current
        {
            get { return this.current; }
        }

        /// <summary>
        /// Advances reader until a complete netstring is found.
        /// </summary>
        /// <exception cref="System.OverflowException" />Raised if value requests a size greater than Int32.MaxLength characters.</exception>
        /// <exception cref="System.IO.InvalidDataException">Raised if value does not strictly adhere to the netstring protocol.</exception>
        /// <returns>false if Netstrings is at the end of the stream.</returns>
        public bool MoveNext()
        {
            int? read = null;

            // do not read on first entry to the loop if there is a backlog in builder and you already have a netstring
            // there  might be another netstring available without consuming from the stream
            while ((builder.Length > 0 && this.current != null) || (read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (read != null)
                {
                    builder.Append(buffer, 0, (int)read);
                }

                // if a netstring is not found on this pass, setting current to null will trigger another read
                this.current = null;
                read = null;

                if (this.size == null)
                {
                    Match match = NetstringReader.SizePattern.Match(builder.ToString());

                    if (match.Success == false)
                    {
                        throw new InvalidDataException("Illegal size field");
                    }

                    if (match.Groups["terminator"].Success)
                    {
                        Group sizeGroup = match.Groups["size"];

                        this.size = Convert.ToInt32(sizeGroup.Value);

                        if (this.size > this.MaxLength)
                        {
                            throw new OverflowException("Requested size exceeded maximum length");
                        }

                        builder.Remove(0, match.Length);
                    }
                    else
                    {
                        if (builder.Length > 10)
                        {
                            throw new OverflowException("Size field exceeded maximum width");
                        }
                    }
                }

                if (this.size != null)
                {
                    int size = (int)this.size;

                    if (builder.Length > size)
                    {
                        char[] output = new char[size];

                        builder.CopyTo(0, output, 0, size);

                        this.current = new String(output);

                        builder.Remove(0, size);

                        if (builder[0] != ',')
                        {
                            throw new InvalidDataException("Payload terminator not found");
                        }

                        builder.Remove(0, 1);

                        this.size = null;

                        return true;
                    }
                }
                else
                {
                    if (builder.Length > 10)
                    {
                        throw new InvalidDataException("Size field terminator not found");
                    }
                }
            }

            if (builder.Length > 0)
            {
                throw new InvalidDataException("Unexpected EOF");
            }

            return false;
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public IEnumerator<string> GetEnumerator()
        {
            return this;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this;
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)。
                    this.reader.Dispose();
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~NetstringReader()
        // {
        //   // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
        //   Dispose(false);
        // }

        // 添加此代码以正确实现可处置模式。
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // TODO: 如果在以上内容中替代了终结器，则取消注释以下行。
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
