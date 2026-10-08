using System;
using System.Text;

namespace client_side
{
    // Length-prefixed message fields. Lengths are UTF-8 byte counts, which is what
    // the server uses, so non-English text (e.g. Hebrew) survives the round trip.
    // (Older code builds fields with string.Length, which only works for ASCII.)
    public static class Wire
    {
        public static string Field(string value) => $"{Encoding.UTF8.GetByteCount(value):D5}{value}";
    }

    public sealed class WireReader
    {
        private readonly byte[] bytes;
        private int pos;

        // Reads a message, skipping its 3-digit code by default.
        public WireReader(string message, int skipChars = 3)
        {
            bytes = Encoding.UTF8.GetBytes(message);
            pos = Encoding.UTF8.GetByteCount(message.Substring(0, Math.Min(skipChars, message.Length)));
        }

        public bool AtEnd => pos >= bytes.Length;

        public string Field(int digits = 5)
        {
            int length = int.Parse(Encoding.ASCII.GetString(bytes, pos, digits));
            pos += digits;
            string value = Encoding.UTF8.GetString(bytes, pos, length);
            pos += length;
            return value;
        }

        public string Take(int byteCount)
        {
            string value = Encoding.UTF8.GetString(bytes, pos, byteCount);
            pos += byteCount;
            return value;
        }

        public string Rest()
        {
            string value = Encoding.UTF8.GetString(bytes, pos, bytes.Length - pos);
            pos = bytes.Length;
            return value;
        }
    }
}
