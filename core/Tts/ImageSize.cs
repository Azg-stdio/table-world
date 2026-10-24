using System.IO;

namespace Core.Tts
{
    /// <summary>Width and height of a PNG or JPEG file from its header, without decoding it (TTS sizes tiles by their image).</summary>
    public static class ImageSize
    {
        public static (int Width, int Height)? Of(string file)
        {
            try
            {
                using var stream = File.OpenRead(file);
                var head = new byte[64 * 1024];
                var read = stream.Read(head, 0, head.Length);
                return Of(head, read);
            }
            catch (IOException)
            {
                return null;
            }
        }

        public static (int Width, int Height)? Of(byte[] data, int length)
        {
            if (length >= 24 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
                return (Big32(data, 16), Big32(data, 20));
            if (length < 4 || data[0] != 0xFF || data[1] != 0xD8)
                return null;
            // JPEG: walk the segments to the frame header (SOF0–SOF15, except DHT, JPG and DAC).
            var i = 2;
            while (i + 9 < length)
            {
                if (data[i] != 0xFF)
                {
                    i++;
                    continue;
                }
                var marker = data[i + 1];
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    return ((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6]);
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    i += 2;
                    continue;
                }
                i += 2 + ((data[i + 2] << 8) | data[i + 3]);
            }
            return null;
        }

        private static int Big32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
    }
}
