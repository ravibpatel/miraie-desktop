#:package Svg.Skia@5.2.3

// Regenerates the app icons from the SVGs next to this file:
//
//   dotnet run art/BuildIcons.cs
//
//   icon-16.svg     16–20 px, snapped to the 16 px grid
//   icon-small.svg  24–32 px, simplified
//   icon.svg        40 px and up
//   icon-macos.svg  macOS, on Apple's icon grid

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using SkiaSharp;
using Svg.Skia;

var artDirectory = Path.GetDirectoryName(SourceFilePath())!;
var root = Path.GetFullPath(Path.Combine(artDirectory, ".."));

int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
Write("MirAIe.Desktop/Assets/app.ico", Ico(icoSizes.Select(size => Render(size switch
{
    <= 20 => "icon-16.svg",
    <= 32 => "icon-small.svg",
    _ => "icon.svg",
}, size)).ToList()));

// PNG-only icns entries, each with the pixel size it holds.
(string Type, int Size)[] icnsEntries =
[
    ("ic11", 32), ("ic12", 64), ("ic07", 128), ("ic13", 256),
    ("ic08", 256), ("ic14", 512), ("ic09", 512), ("ic10", 1024),
];
Write("packaging/macos/MirAIe.icns", Icns(icnsEntries.Select(e => (e.Type, Render("icon-macos.svg", e.Size))).ToList()));

Write("packaging/linux/miraie-desktop.png", Render("icon.svg", 256));

byte[] Render(string svgFile, int size)
{
    using var svg = new SKSvg();
    var picture = svg.Load(Path.Combine(artDirectory, svgFile)) ?? throw new InvalidOperationException($"Couldn't load {svgFile}.");
    using var surface = SKSurface.Create(new SKImageInfo(size, size));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.Scale(size / picture.CullRect.Width);
    surface.Canvas.DrawPicture(picture);
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// ICO: a 6-byte header and a 16-byte entry per image, followed by the PNGs.
static byte[] Ico(List<byte[]> pngs)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)pngs.Count);
    var offset = 6 + 16 * pngs.Count;
    foreach (var png in pngs)
    {
        var size = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)); // IHDR width
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(png.Length);
        writer.Write(offset);
        offset += png.Length;
    }
    foreach (var png in pngs)
        writer.Write(png);
    return stream.ToArray();
}

// ICNS: big-endian "icns" + total length, then type + length (including its 8-byte header) + PNG per entry.
static byte[] Icns(List<(string Type, byte[] Png)> entries)
{
    var length = 8 + entries.Sum(e => 8 + e.Png.Length);
    var icns = new byte[length];
    var span = icns.AsSpan();
    "icns"u8.CopyTo(span);
    BinaryPrimitives.WriteInt32BigEndian(span[4..], length);
    var offset = 8;
    foreach (var (type, png) in entries)
    {
        System.Text.Encoding.ASCII.GetBytes(type, span[offset..]);
        BinaryPrimitives.WriteInt32BigEndian(span[(offset + 4)..], 8 + png.Length);
        png.CopyTo(span[(offset + 8)..]);
        offset += 8 + png.Length;
    }
    return icns;
}

void Write(string relativePath, byte[] bytes)
{
    var path = Path.Combine(root, relativePath);
    File.WriteAllBytes(path, bytes);
    Console.WriteLine($"{relativePath} ({bytes.Length} bytes)");
}

static string SourceFilePath([CallerFilePath] string path = "") => path;
