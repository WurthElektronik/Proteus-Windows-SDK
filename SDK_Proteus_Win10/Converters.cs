using System;
using System.Text;

namespace WE_eiSos_BluetoothLE
{
    class Converters
    {
        public static byte[] StringToByteArray(string hexString)
        {
            /// the string must have an even numer of characters
            if ((hexString.Length % 2) == 1)
            {
                return null;
            }

            byte[] bytes = new byte[hexString.Length / 2]; /// 2 characters per byte

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
            {
                hex.AppendFormat("{0:x2}", b);
            }

            return hex.ToString().ToUpper(); /// hexadecimal coded
        }
    }
}
