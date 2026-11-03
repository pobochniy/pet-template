using System.Buffers.Binary;
using System.Text;

namespace PetTemplate.Services.Assistant.Transcription;

/// <summary>
/// Длительность записи по контейнеру, без декодирования (спец. assistant, 6.1): то, что
/// пишет <c>MediaRecorder</c> браузеров, — WebM (Chrome, Firefox), MP4 (Safari) и Ogg.
/// Запись живая: у WebM в заголовке нет <c>Duration</c>, у фрагментированного MP4 длительность
/// в <c>mvhd</c> нулевая, — поэтому длительность берётся по меткам времени последних блоков.
/// Контейнер не узнан или битый — <c>null</c>: тогда судят движок и потолок размера
/// </summary>
public static class AudioDuration
{
    /// <summary>Opus всегда считает гранулы в 48 кГц, какой бы ни была частота входа</summary>
    private const double OpusGranuleRate = 48_000;

    /// <summary>Единица меток WebM по умолчанию — миллисекунда, в наносекундах</summary>
    private const ulong DefaultTimecodeScale = 1_000_000;

    private const double NanosecondsPerSecond = 1_000_000_000;

    public static double? Seconds(ReadOnlySpan<byte> audio)
    {
        try
        {
            if (audio.StartsWith("OggS"u8)) return Ogg(audio);
            if (audio.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3])) return WebM(audio);
            if (audio.Length >= 8 && audio.Slice(4, 4).SequenceEqual("ftyp"u8)) return Mp4(audio);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            // Обрезанный или битый контейнер — длительность неизвестна
        }

        return null;
    }

    /// <summary>
    /// Ogg: гранула последней страницы потока. У Opus — 48 кГц минус <c>pre-skip</c>
    /// из <c>OpusHead</c>, у Vorbis — частота из заголовка
    /// </summary>
    private static double? Ogg(ReadOnlySpan<byte> audio)
    {
        double? rate = null;
        long preSkip = 0;
        long last = -1;
        var offset = 0;

        while (offset + 27 <= audio.Length && audio.Slice(offset, 4).SequenceEqual("OggS"u8))
        {
            var granule = BinaryPrimitives.ReadInt64LittleEndian(audio.Slice(offset + 6, 8));
            int segments = audio[offset + 26];
            var bodyLength = 0;
            for (var i = 0; i < segments; i++) bodyLength += audio[offset + 27 + i];
            var body = audio.Slice(offset + 27 + segments, bodyLength);

            if (rate == null && body.StartsWith("OpusHead"u8))
            {
                rate = OpusGranuleRate;
                preSkip = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(10, 2));
            }
            else if (rate == null && body.Length >= 16 && body[0] == 1 && body.Slice(1, 6).SequenceEqual("vorbis"u8))
            {
                rate = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(12, 4));
            }

            if (granule >= 0) last = granule;
            offset += 27 + segments + bodyLength;
        }

        if (rate is not > 0 || last < 0) return null;

        return Math.Max(0, last - preSkip) / rate.Value;
    }

    /// <summary>
    /// WebM (Matroska): <c>Duration</c> из <c>Info</c>, если записан, иначе — самая поздняя
    /// метка блока: метка кластера плюс сдвиг блока. Сегмент и кластеры живой записи —
    /// неизвестного размера, поэтому они не перепрыгиваются, а читаются подряд
    /// </summary>
    private static double? WebM(ReadOnlySpan<byte> audio)
    {
        const uint segment = 0x18538067, cluster = 0x1F43B675, info = 0x1549A966, blockGroup = 0xA0;
        const uint timecodeScale = 0x2AD7B1, duration = 0x4489, timecode = 0xE7, simpleBlock = 0xA3, block = 0xA1;

        var scale = DefaultTimecodeScale;
        double? declared = null;
        ulong clusterTime = 0;
        long latest = -1;
        var offset = 0;

        while (offset < audio.Length)
        {
            var id = ReadId(audio, ref offset);
            var size = ReadSize(audio, ref offset, out var unknown);
            if (id is segment or cluster or info or blockGroup) continue;
            if (unknown) break;

            var length = (int)Math.Min(size, (ulong)(audio.Length - offset));
            var data = audio.Slice(offset, length);
            switch (id)
            {
                case timecodeScale:
                    scale = ReadUnsigned(data);
                    break;
                case duration:
                    declared = data.Length == 4
                        ? BinaryPrimitives.ReadSingleBigEndian(data)
                        : BinaryPrimitives.ReadDoubleBigEndian(data);
                    break;
                case timecode:
                    clusterTime = ReadUnsigned(data);
                    break;
                case simpleBlock or block:
                    var at = 0;
                    ReadSize(data, ref at, out _); // номер дорожки
                    var relative = BinaryPrimitives.ReadInt16BigEndian(data.Slice(at, 2));
                    latest = Math.Max(latest, (long)clusterTime + relative);
                    break;
            }

            offset += length;
        }

        if (declared > 0) return declared.Value * scale / NanosecondsPerSecond;

        return latest < 0 ? null : latest * (double)scale / NanosecondsPerSecond;
    }

    /// <summary>
    /// MP4: длительность дорожки из <c>mdhd</c> (или ролика из <c>mvhd</c>), а у
    /// фрагментированного — конец последнего фрагмента: <c>tfdt</c> плюс длительности
    /// сэмплов <c>trun</c> (свои или по умолчанию из <c>tfhd</c>/<c>trex</c>)
    /// </summary>
    private static double? Mp4(ReadOnlySpan<byte> audio)
    {
        var state = new Mp4State();
        Mp4Boxes(audio, state);

        var best = state.MovieTimescale > 0 ? state.MovieDuration / (double)state.MovieTimescale : 0;
        foreach (var (track, timescale) in state.Timescales)
        {
            if (timescale == 0) continue;
            var fragments = state.FragmentEnd.GetValueOrDefault(track);
            var header = state.TrackDuration.GetValueOrDefault(track);
            best = Math.Max(best, Math.Max(fragments, header) / (double)timescale);
        }

        return best > 0 ? best : null;
    }

    private static void Mp4Boxes(ReadOnlySpan<byte> data, Mp4State state)
    {
        var offset = 0;
        while (offset + 8 <= data.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            var type = Encoding.ASCII.GetString(data.Slice(offset + 4, 4));
            var header = 8;
            if (size == 1)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset + 8, 8));
                header = 16;
            }
            else if (size == 0)
            {
                size = data.Length - offset;
            }

            if (size < header) return;

            var length = (int)Math.Min(size, data.Length - offset);
            var body = data.Slice(offset + header, length - header);
            Mp4Box(type, body, state);
            offset += length;
        }
    }

    private static void Mp4Box(string type, ReadOnlySpan<byte> body, Mp4State state)
    {
        switch (type)
        {
            case "moov" or "trak" or "mdia" or "mvex" or "moof":
                Mp4Boxes(body, state);
                break;
            case "traf":
                state.Fragment = new Mp4Fragment();
                Mp4Boxes(body, state);
                var fragment = state.Fragment;
                var start = fragment.BaseTime ?? state.FragmentEnd.GetValueOrDefault(fragment.Track);
                state.FragmentEnd[fragment.Track] = Math.Max(
                    state.FragmentEnd.GetValueOrDefault(fragment.Track), start + fragment.Duration);
                break;
            case "mvhd":
                var v1 = body[0] == 1;
                state.MovieTimescale = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(v1 ? 20 : 12, 4));
                state.MovieDuration = ReadDuration(body.Slice(v1 ? 24 : 16), v1);
                break;
            case "tkhd":
                state.Track = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(body[0] == 1 ? 20 : 12, 4));
                break;
            case "mdhd":
                var m1 = body[0] == 1;
                state.Timescales[state.Track] = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(m1 ? 20 : 12, 4));
                state.TrackDuration[state.Track] = ReadDuration(body.Slice(m1 ? 24 : 16), m1);
                break;
            case "trex":
                state.DefaultDurations[BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4))] =
                    BinaryPrimitives.ReadUInt32BigEndian(body.Slice(12, 4));
                break;
            case "tfhd":
                TrackFragmentHeader(body, state);
                break;
            case "tfdt":
                state.Fragment.BaseTime = body[0] == 1
                    ? BinaryPrimitives.ReadUInt64BigEndian(body.Slice(4, 8))
                    : BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4));
                break;
            case "trun":
                TrackRun(body, state);
                break;
        }
    }

    private static void TrackFragmentHeader(ReadOnlySpan<byte> body, Mp4State state)
    {
        var flags = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(0, 4)) & 0xFFFFFF;
        var fragment = state.Fragment;
        fragment.Track = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4));
        fragment.DefaultDuration = state.DefaultDurations.GetValueOrDefault(fragment.Track);

        var at = 8;
        if ((flags & 0x01) != 0) at += 8; // base-data-offset
        if ((flags & 0x02) != 0) at += 4; // sample-description-index
        if ((flags & 0x08) != 0) fragment.DefaultDuration = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(at, 4));
    }

    private static void TrackRun(ReadOnlySpan<byte> body, Mp4State state)
    {
        var flags = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(0, 4)) & 0xFFFFFF;
        var count = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4));
        var at = 8;
        if ((flags & 0x001) != 0) at += 4; // data-offset
        if ((flags & 0x004) != 0) at += 4; // first-sample-flags

        var fragment = state.Fragment;
        if ((flags & 0x100) == 0)
        {
            fragment.Duration += count * fragment.DefaultDuration;

            return;
        }

        var stride = 4 * (1 + ((flags & 0x200) != 0 ? 1 : 0) + ((flags & 0x400) != 0 ? 1 : 0) + ((flags & 0x800) != 0 ? 1 : 0));
        for (var i = 0; i < count; i++)
        {
            fragment.Duration += BinaryPrimitives.ReadUInt32BigEndian(body.Slice(at + i * stride, 4));
        }
    }

    private static ulong ReadDuration(ReadOnlySpan<byte> data, bool wide)
    {
        // Все единицы — «длительность неизвестна»
        if (wide)
        {
            var value = BinaryPrimitives.ReadUInt64BigEndian(data);

            return value == ulong.MaxValue ? 0 : value;
        }

        var narrow = BinaryPrimitives.ReadUInt32BigEndian(data);

        return narrow == uint.MaxValue ? 0 : narrow;
    }

    /// <summary>Идентификатор элемента EBML — со своим маркером длины</summary>
    private static uint ReadId(ReadOnlySpan<byte> data, ref int offset)
    {
        // Длина идентификатора — по старшему единичному биту первого байта, от одного до четырёх
        var first = data[offset];
        var length = 1;
        while (length <= 4 && (first & (0x80 >> (length - 1))) == 0) length++;
        if (length > 4) throw new ArgumentOutOfRangeException(nameof(data), "Invalid EBML id");

        uint id = 0;
        for (var i = 0; i < length; i++) id = (id << 8) | data[offset + i];
        offset += length;

        return id;
    }

    /// <summary>Размер элемента EBML: маркер длины снимается; все единицы — «неизвестен»</summary>
    private static ulong ReadSize(ReadOnlySpan<byte> data, ref int offset, out bool unknown)
    {
        var first = data[offset];
        var length = 1;
        while (length <= 8 && (first & (0x80 >> (length - 1))) == 0) length++;
        if (length > 8) throw new ArgumentOutOfRangeException(nameof(data), "Invalid EBML size");

        ulong value = (ulong)(first & (0xFF >> length));
        var allOnes = value == (ulong)(0xFF >> length);
        for (var i = 1; i < length; i++)
        {
            value = (value << 8) | data[offset + i];
            allOnes &= data[offset + i] == 0xFF;
        }

        offset += length;
        unknown = allOnes;

        return value;
    }

    private static ulong ReadUnsigned(ReadOnlySpan<byte> data)
    {
        ulong value = 0;
        foreach (var b in data) value = (value << 8) | b;

        return value;
    }

    private sealed class Mp4State
    {
        public uint MovieTimescale { get; set; }

        public ulong MovieDuration { get; set; }

        public uint Track { get; set; }

        public Dictionary<uint, uint> Timescales { get; } = [];

        public Dictionary<uint, ulong> TrackDuration { get; } = [];

        public Dictionary<uint, uint> DefaultDurations { get; } = [];

        public Dictionary<uint, ulong> FragmentEnd { get; } = [];

        public Mp4Fragment Fragment { get; set; } = new();
    }

    private sealed class Mp4Fragment
    {
        public uint Track { get; set; }

        public ulong? BaseTime { get; set; }

        public uint DefaultDuration { get; set; }

        public ulong Duration { get; set; }
    }
}
