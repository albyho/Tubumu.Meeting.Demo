using System;
using System.IO;

namespace TubumuMeeting.Netstrings
{
    public class NetstringWriter
    {
        /// <summary>
        /// Emits the specified string as a netstring.
        /// </summary>
        /// <param name="value">The string to encode as a netstring.</param>
        /// <returns>A netstring.</returns>
        public static string Encode(string value)
        {
            return String.Format("{0}:{1},", value.Length, value);
        }

        private readonly TextWriter writer;

        public NetstringWriter(TextWriter writer)
        {
            this.writer = writer;
        }

        public void Write(string value)
        {
            this.writer.Write(Encode(value));
        }

        public void WriteLine(string value)
        {
            this.writer.Write(Encode(String.Concat(value, Environment.NewLine)));
        }

        public void Flush()
        {
            this.writer.Flush();
        }
    }
}
