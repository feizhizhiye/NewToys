using System;
using System.Buffers.Binary;
using System.IO;

namespace NewToys.Viewer.Controls;

/// <summary>
/// 图片头部尺寸探测：只读文件头拿原始像素尺寸，**不解码像素**。
/// <para>
/// 🔴 用途只有一个：决定是否走降采样解码（<c>Bitmap.DecodeToWidth</c>）——
/// 该 API 在目标尺寸大于原图时**会放大**，所以必须先知道原图多宽，否则小图反而被放大吃更多内存。
/// </para>
/// <para>支持 png / jpeg / gif / bmp / webp；探测失败返回 <c>false</c>，调用方退回整图解码。</para>
/// </summary>
public static class ImageHeader
{
    /// <summary>读取图片原始像素尺寸（读完把流位置复位到读之前；不可寻址的流不探测）。</summary>
    public static bool TryReadSize(Stream stream, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (stream is null || !stream.CanSeek) return false;

        var origin = stream.Position;
        try
        {
            Span<byte> head = stackalloc byte[32];
            var read = stream.Read(head);
            if (read < 12) return false;
            head = head[..read];

            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
                return TryPng(stream, origin, out width, out height);

            if (head[0] == 0xFF && head[1] == 0xD8)
                return TryJpeg(stream, origin, out width, out height);

            if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F')
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(head[6..]);
                height = BinaryPrimitives.ReadUInt16LittleEndian(head[8..]);
                return width > 0 && height > 0;
            }

            if (head[0] == 'B' && head[1] == 'M')
            {
                stream.Position = origin;
                Span<byte> bmp = stackalloc byte[26];
                if (stream.Read(bmp) < 26) return false;
                width = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bmp[18..]));
                height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bmp[22..]));
                return width > 0 && height > 0;
            }

            if (read >= 16 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
                return TryWebp(stream, origin, head, out width, out height);

            return false;
        }
        catch
        {
            return false;   // 头部异常 ⇒ 当探测失败，走整图解码（解码本身会报真正的错）
        }
        finally
        {
            try { stream.Position = origin; } catch { /* 复位失败交给调用方处理 */ }
        }
    }

    private static bool TryPng(Stream stream, long origin, out int width, out int height)
    {
        width = 0;
        height = 0;
        stream.Position = origin + 16;
        Span<byte> size = stackalloc byte[8];
        if (stream.Read(size) < 8) return false;

        width = BinaryPrimitives.ReadInt32BigEndian(size[..4]);
        height = BinaryPrimitives.ReadInt32BigEndian(size[4..]);
        return width > 0 && height > 0;
    }

    /// <summary>JPEG：逐段扫描到 SOF0~SOF15（跳过 C4 / C8 / CC，它们是 Huffman / 算术表与 RST 标记）。</summary>
    private static bool TryJpeg(Stream stream, long origin, out int width, out int height)
    {
        width = 0;
        height = 0;

        stream.Position = origin + 2;
        Span<byte> pair = stackalloc byte[2];

        while (true)
        {
            // 找标记：段之间可能有填充 0xFF
            int b;
            do
            {
                b = stream.ReadByte();
                if (b < 0) return false;
            }
            while (b != 0xFF);

            int marker;
            do
            {
                marker = stream.ReadByte();
                if (marker < 0) return false;
            }
            while (marker == 0xFF);

            if (marker == 0xD8 || marker == 0xD9) continue;   // SOI / EOI 无长度字段
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;   // TEM / RSTn 无长度字段

            if (stream.Read(pair) < 2) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(pair);
            if (length < 2) return false;

            var isSof = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isSof)
            {
                Span<byte> sof = stackalloc byte[5];   // precision(1) + height(2) + width(2)
                if (stream.Read(sof) < 5) return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(sof[1..3]);
                width = BinaryPrimitives.ReadUInt16BigEndian(sof[3..5]);
                return width > 0 && height > 0 && height != 0xFFFF;
            }

            stream.Position += length - 2;
        }
    }

    private static bool TryWebp(Stream stream, long origin, Span<byte> head, out int width, out int height)
    {
        width = 0;
        height = 0;

        var chunk = System.Text.Encoding.ASCII.GetString(head[12..16].ToArray());

        Span<byte> buffer = stackalloc byte[10];
        stream.Position = origin + 20;

        if (chunk == "VP8X")
        {
            // flags(1) + reserved(3) + canvasWidth-1(3) + canvasHeight-1(3)
            if (stream.Read(buffer) < 10) return false;
            width = 1 + (buffer[4] | (buffer[5] << 8) | (buffer[6] << 16));
            height = 1 + (buffer[7] | (buffer[8] << 8) | (buffer[9] << 16));
            return width > 0 && height > 0;
        }

        if (chunk == "VP8L")
        {
            if (stream.Read(buffer[..5]) < 5) return false;
            if (buffer[0] != 0x2F) return false;
            var bits = buffer[1] | (buffer[2] << 8) | (buffer[3] << 16) | (buffer[4] << 24);
            width = (bits & 0x3FFF) + 1;
            height = ((bits >> 14) & 0x3FFF) + 1;
            return true;
        }

        if (chunk == "VP8 ")
        {
            // 帧头 3 字节（9D 01 2A）+ 宽 2 字节 + 高 2 字节（各取低 14 位）
            if (stream.Read(buffer[..7]) < 7) return false;
            if (buffer[0] != 0x9D || buffer[1] != 0x01 || buffer[2] != 0x2A) return false;
            width = BinaryPrimitives.ReadUInt16LittleEndian(buffer[3..5]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(buffer[5..7]) & 0x3FFF;
            return width > 0 && height > 0;
        }

        return false;
    }
}
