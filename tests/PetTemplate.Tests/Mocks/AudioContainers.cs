using System.Buffers.Binary;
using System.Text;

namespace PetTemplate.Tests.Mocks;

/// <summary>
/// Записи-заглушки в контейнерах браузерного <c>MediaRecorder</c> (спец. assistant, 6.1):
/// только заголовки и метки времени — звука в них нет, сервер его и не декодирует.
/// Устроены как живая запись: WebM без <c>Duration</c> и с кластерами неизвестного размера,
/// MP4 — фрагментированный, с нулевой длительностью в <c>mvhd</c>
/// </summary>
public static class AudioContainers
{
    /// <summary>WebM (Chrome, Firefox): кластер в секунду, блоки по 20 мс</summary>
    public static byte[] WebM(double seconds)
    {
        var body = new List<byte>();
        body.AddRange(Ebml(0x1549A966, Ebml(0x2AD7B1, [0x0F, 0x42, 0x40]))); // Info: TimecodeScale = 1 мс
        var totalMs = (int)(seconds * 1000);
        for (var cluster = 0; cluster * 1000 < totalMs; cluster++)
        {
            body.AddRange([0x1F, 0x43, 0xB6, 0x75, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]); // Cluster, размер неизвестен
            body.AddRange(Ebml(0xE7, BigEndian((uint)(cluster * 1000))));
            for (var at = 0; at < 1000 && cluster * 1000 + at < totalMs; at += 20)
            {
                body.AddRange(Ebml(0xA3, [0x81, (byte)(at >> 8), (byte)at, 0x80, 0xFC]));
            }
        }

        var file = new List<byte>();
        file.AddRange(Ebml(0x1A45DFA3, Ebml(0x4282, "webm"u8.ToArray()))); // EBML, DocType
        file.AddRange([0x18, 0x53, 0x80, 0x67, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]); // Segment, размер неизвестен
        file.AddRange(body);

        return [.. file];
    }

    /// <summary>Ogg Opus: страница заголовка и страница с гранулой конца (48 кГц плюс pre-skip)</summary>
    public static byte[] OggOpus(double seconds)
    {
        const ushort preSkip = 312;
        var head = new byte[19];
        "OpusHead"u8.CopyTo(head);
        head[8] = 1;
        head[9] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(10), preSkip);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), 48_000);

        return [.. OggPage(0, head), .. OggPage((long)(seconds * 48_000) + preSkip, new byte[40])];
    }

    /// <summary>Фрагментированный MP4 (Safari): AAC 48 кГц, кадр 1024 сэмпла, фрагмент в секунду</summary>
    public static byte[] FragmentedMp4(double seconds)
    {
        const uint timescale = 48_000, frame = 1024;
        var frames = (uint)Math.Ceiling(seconds * timescale / frame);

        var file = new List<byte>();
        file.AddRange(Box("ftyp", [.. "iso5"u8, 0, 0, 2, 0, .. "iso5"u8]));
        file.AddRange(Box("moov",
            Box("mvhd", FullBox(0, [.. new byte[8], .. BigEndian(1000), .. BigEndian(0), .. new byte[80]])),
            Box("trak",
                Box("tkhd", FullBox(0, [.. new byte[8], .. BigEndian(1), .. new byte[68]])),
                Box("mdia", Box("mdhd", FullBox(0, [.. new byte[8], .. BigEndian(timescale), .. BigEndian(0), 0, 0, 0, 0])))),
            Box("mvex", Box("trex", FullBox(0, [.. BigEndian(1), .. BigEndian(1), .. BigEndian(frame), .. BigEndian(0), .. BigEndian(0)])))));

        ulong decoded = 0;
        var perFragment = timescale / frame;
        for (uint done = 0; done < frames; done += perFragment)
        {
            var count = Math.Min(perFragment, frames - done);
            file.AddRange(Box("moof",
                Box("traf",
                    Box("tfhd", FullBox(0x020000, BigEndian(1))),
                    Box("tfdt", FullBox(0, [.. BigEndian((uint)decoded)], version: 0)),
                    // Длительности у каждого сэмпла свои (флаг 0x100) плюс размер (0x200)
                    Box("trun", FullBox(0x000301, [
                        .. BigEndian(count), .. BigEndian(0),
                        .. Enumerable.Range(0, (int)count).SelectMany(_ => (byte[])[.. BigEndian(frame), .. BigEndian(4)])
                    ])))));
            file.AddRange(Box("mdat", new byte[count * 4]));
            decoded += count * frame;
        }

        return [.. file];
    }

    /// <summary>Обычный MP4: длительность записана в <c>mvhd</c> и <c>mdhd</c></summary>
    public static byte[] Mp4(double seconds)
    {
        const uint timescale = 44_100;
        var duration = (uint)(seconds * timescale);

        return
        [
            .. Box("ftyp", [.. "M4A "u8, 0, 0, 0, 0]),
            .. Box("moov",
                Box("mvhd", FullBox(0, [.. new byte[8], .. BigEndian(1000), .. BigEndian((uint)(seconds * 1000)), .. new byte[80]])),
                Box("trak",
                    Box("tkhd", FullBox(0, [.. new byte[8], .. BigEndian(1), .. new byte[68]])),
                    Box("mdia", Box("mdhd", FullBox(0, [.. new byte[8], .. BigEndian(timescale), .. BigEndian(duration), 0, 0, 0, 0]))))),
            .. Box("mdat", new byte[16])
        ];
    }

    private static byte[] OggPage(long granule, byte[] body)
    {
        var page = new List<byte>();
        page.AddRange("OggS"u8.ToArray());
        page.Add(0);
        page.Add(granule == 0 ? (byte)2 : (byte)4);
        var g = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(g, granule);
        page.AddRange(g);
        page.AddRange(new byte[12]); // serial, sequence, crc
        page.Add(1);
        page.Add((byte)body.Length);
        page.AddRange(body);

        return [.. page];
    }

    private static byte[] Ebml(uint id, byte[] data)
    {
        var bytes = new List<byte>();
        var idBytes = BigEndian(id).SkipWhile(x => x == 0).ToArray();
        bytes.AddRange(idBytes);
        // Размер — восемь байт с маркером: так любой размер помещается
        bytes.Add(0x01);
        var size = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)data.Length);
        bytes.AddRange(size.Skip(1));
        bytes.AddRange(data);

        return [.. bytes];
    }

    private static byte[] Box(string type, params byte[][] children)
    {
        var body = children.SelectMany(x => x).ToArray();

        return [.. BigEndian((uint)(8 + body.Length)), .. Encoding.ASCII.GetBytes(type), .. body];
    }

    private static byte[] FullBox(uint flags, byte[] body, byte version = 0) =>
        [.. BigEndian((uint)(version << 24) | flags), .. body];

    private static byte[] BigEndian(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);

        return bytes;
    }
}
