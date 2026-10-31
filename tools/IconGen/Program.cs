using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// 生成 WidgetNest 应用图标：圆角渐变底 + 2x2 组件格子。
// 输出多尺寸 .ico（16/24/32/48/64/128/256）与 256px PNG 预览。
//
//   IconGen <输出.ico>                 生成图标
//   IconGen --extract <目标.exe> [png]  提取可执行文件里嵌入的图标，用于核对发布结果

if (args.Length >= 2 && args[0] == "--extract") {
    var exePath = args[1];
    var pngOut = args.Length >= 3 ? args[2] : Path.ChangeExtension(exePath, ".png");

    using var icon = Icon.ExtractAssociatedIcon(exePath)
                     ?? throw new InvalidOperationException("该文件没有关联图标");
    using var bmp = icon.ToBitmap();
    bmp.Save(pngOut, ImageFormat.Png);

    Console.WriteLine($"extracted {bmp.Width}x{bmp.Height} -> {pngOut}");
    return;
}

var output = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "app.ico");

var previewPath = Path.ChangeExtension(output, ".png");

int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var frames = new List<(int Size, byte[] Dib)>();

foreach (var size in sizes) {
    using var bmp = Render(size);
    frames.Add((size, ToDib(bmp)));
    if (size == 256) bmp.Save(previewPath, ImageFormat.Png);
}

WriteIco(output, frames);
Console.WriteLine($"icon  -> {output}  ({new FileInfo(output).Length / 1024.0:F1} KB)");
Console.WriteLine($"preview -> {previewPath}");

// ---------- 绘制 ----------

static Bitmap Render(int size) {
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);

    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    float s = size;
    bool tiny = size <= 24;

    // 底板：圆角矩形 + 45° 蓝紫渐变（与应用主色 #4C8DFF 一致）
    float inset = s * 0.02f;
    var plate = new RectangleF(inset, inset, s - inset * 2, s - inset * 2);
    float plateRadius = s * 0.23f;

    using (var path = RoundedRect(plate, plateRadius))
    using (var brush = new LinearGradientBrush(
               new RectangleF(0, 0, s, s),
               Color.FromArgb(255, 0x5B, 0x93, 0xFF),
               Color.FromArgb(255, 0x6C, 0x5C, 0xE7),
               45f)) {
        g.FillPath(brush, path);
    }

    // 顶部高光，让纯色块不至于太平。
    // 渐变矩形必须铺满整块底板：只覆盖上半部分的话，超出部分会被夹取填充，
    // 在边界处留下一条横向色带。
    if (!tiny) {
        using var hl = new LinearGradientBrush(
            new RectangleF(0, 0, s, s),
            Color.FromArgb(0, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            LinearGradientMode.Vertical);

        hl.InterpolationColors = new ColorBlend {
            Positions = [0f, 0.52f, 1f],
            Colors = [
                Color.FromArgb(72, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255)
            ]
        };

        using var hlPath = RoundedRect(plate, plateRadius);
        g.FillPath(hl, hlPath);
    }

    // 2x2 组件格子：右下角更淡，制造层次
    float block = s * (tiny ? 0.305f : 0.255f);
    float gap = s * (tiny ? 0.095f : 0.115f);
    float total = block * 2 + gap;
    float x0 = (s - total) / 2f;
    float y0 = (s - total) / 2f;
    float blockRadius = block * (tiny ? 0.16f : 0.24f);

    int[] alphas = [255, 200, 200, 140];

    for (int i = 0; i < 4; i++) {
        float x = x0 + (i % 2) * (block + gap);
        float y = y0 + (i / 2) * (block + gap);

        using var cell = RoundedRect(new RectangleF(x, y, block, block), blockRadius);
        using var cellBrush = new SolidBrush(Color.FromArgb(alphas[i], 255, 255, 255));
        g.FillPath(cellBrush, cell);
    }

    return bmp;
}

static GraphicsPath RoundedRect(RectangleF r, float radius) {
    var path = new GraphicsPath();

    // 半径过大时退化成胶囊，避免出现自交
    radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
    float d = radius * 2f;

    if (radius <= 0.1f) {
        path.AddRectangle(r);
        path.CloseFigure();
        return path;
    }

    path.AddArc(r.X, r.Y, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

// ---------- ICO 编码 ----------

// 单帧 32bpp DIB：BITMAPINFOHEADER + 自下而上的 BGRA + 全零 AND mask
static byte[] ToDib(Bitmap bmp) {
    int w = bmp.Width, h = bmp.Height;

    var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    int stride = data.Stride;
    var pixels = new byte[stride * h];
    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
    bmp.UnlockBits(data);

    int andStride = ((w + 31) / 32) * 4;
    int xorSize = w * h * 4;
    int andSize = andStride * h;

    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    bw.Write(40);                 // biSize
    bw.Write(w);                  // biWidth
    bw.Write(h * 2);              // biHeight（XOR + AND 两段）
    bw.Write((short)1);           // biPlanes
    bw.Write((short)32);          // biBitCount
    bw.Write(0);                  // biCompression = BI_RGB
    bw.Write(xorSize + andSize);  // biSizeImage
    bw.Write(0);                  // XPelsPerMeter
    bw.Write(0);                  // YPelsPerMeter
    bw.Write(0);                  // biClrUsed
    bw.Write(0);                  // biClrImportant

    for (int y = h - 1; y >= 0; y--)
        bw.Write(pixels, y * stride, w * 4);

    bw.Write(new byte[andSize]);  // 32bpp 用 alpha，掩码置零

    bw.Flush();
    return ms.ToArray();
}

static void WriteIco(string path, List<(int Size, byte[] Dib)> frames) {
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);

    bw.Write((short)0);            // reserved
    bw.Write((short)1);            // type = icon
    bw.Write((short)frames.Count);

    int offset = 6 + 16 * frames.Count;

    foreach (var (size, dib) in frames) {
        byte dim = size >= 256 ? (byte)0 : (byte)size;

        bw.Write(dim);             // width
        bw.Write(dim);             // height
        bw.Write((byte)0);         // palette
        bw.Write((byte)0);         // reserved
        bw.Write((short)1);        // planes
        bw.Write((short)32);       // bit count
        bw.Write(dib.Length);      // bytes
        bw.Write(offset);          // offset
        offset += dib.Length;
    }

    foreach (var (_, dib) in frames)
        bw.Write(dib);

    bw.Flush();
}
