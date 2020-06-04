using System;
using System.Text;

namespace TubumuMeeting.Netstring
{
    public static class NetStringExtensions
    {
        public static string DecodeFromArraySegment(this ArraySegment<byte> arraySegment)
        {
            return NetstringReader.Decode(Encoding.UTF8.GetString(arraySegment.Array, arraySegment.Offset, arraySegment.Count));
        }

        public static ArraySegment<byte> EncodeToArraySegment(this string source)
        {
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(NetstringWriter.Encode(source)));
        }
    }
}
