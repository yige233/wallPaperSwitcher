using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;

namespace WallpaperSwitcher
{
    public static class Image
    {
        private static readonly byte[] JPG = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] PNG = { 0x89, 0x50, 0x4E, 0x47 };
        private static readonly byte[] BMP = { 0x42, 0x4D };
        private static readonly byte[] TIFF_II = { 0x49, 0x49, 0x2A, 0x00 };
        private static readonly byte[] TIFF_MM = { 0x4D, 0x4D, 0x00, 0x2A };
        private static readonly byte[] WEBP_RIFF = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] WEBP_WEBP = { 0x57, 0x45, 0x42, 0x50 };
        private static readonly byte[] FTYP = { 0x66, 0x74, 0x79, 0x70 };

        public static string GetImageFormat(byte[] data)
        {
            if (data == null || data.Length < 12) return "unknown";

            if (StartsWith(data, JPG)) return "jpg";
            if (StartsWith(data, PNG)) return "png";
            if (StartsWith(data, BMP)) return "bmp";
            if (StartsWith(data, TIFF_II) || StartsWith(data, TIFF_MM)) return "tiff";

            // WebP
            if (StartsWith(data, WEBP_RIFF) &&
                data[8] == WEBP_WEBP[0] && data[9] == WEBP_WEBP[1] &&
                data[10] == WEBP_WEBP[2] && data[11] == WEBP_WEBP[3])
            {
                return "webp";
            }

            // HEIC / AVIF (ISO Base Media File Format)
            if (data[4] == FTYP[0] && data[5] == FTYP[1] && data[6] == FTYP[2] && data[7] == FTYP[3])
            {
                string brand = Encoding.ASCII.GetString(data, 8, 4).ToLower();
                if (new[] { "heic", "heix", "mif1", "msf1" }.Contains(brand)) return "heic";
                if (new[] { "avif", "avis" }.Contains(brand)) return "avif";
            }

            return "unknown";
        }

        public static string GetImageFormat(string path)
        {
            if (!File.Exists(path)) return "unknown";
            try
            {
                byte[] buffer = new byte[12];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Read(buffer, 0, 12) < 12) return "unknown";
                }
                return GetImageFormat(buffer);
            }
            catch { return "unknown"; }
        }


        public static bool IsSafeFormat(string fmt) { return fmt == "jpg" || fmt == "png" || fmt == "bmp" || fmt == "tiff"; }

        public static bool IsExtraFormat(string fmt)
        {
            string configStr = Config.Read("ExtraFormat");
            if (string.IsNullOrEmpty(configStr)) return false;
            string[] allowedExtras = configStr.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var extra in allowedExtras)
            {
                if (extra.Trim().Equals(fmt, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        private static bool StartsWith(byte[] data, byte[] magic)
        {
            for (int i = 0; i < magic.Length; i++) if (data[i] != magic[i]) return false;
            return true;
        }
        public static bool SaveImage(string url, string savePath, long jpgQuality = 95L)
        {
            try
            {
                byte[] data = Downloader.Download(url);
                if (data == null) return false;
                string format = GetImageFormat(data);

                if (format == "unknown")
                {
                    string headHex = BitConverter.ToString(data.Take(8).ToArray());
                    App.Log(string.Format("未知文件：{0}", headHex), true);
                    return false;
                }
                if (IsSafeFormat(format))
                {
                    if (jpgQuality == 100L) { SaveDirectly(data, savePath); }
                    else { SaveWithGDI(data, savePath, jpgQuality); }
                    return true;
                }

                if (IsExtraFormat(format))
                {
                    SaveDirectly(data, savePath);
                    return true;
                }

                App.Log(string.Format("不支持的文件格式：{0}", format), true);
                return false;
            }
            catch (Exception ex)
            {
                App.Log("保存 " + url + " 失败: " + ex.Message, true);
                return false;
            }
            finally
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        private static void SaveWithGDI(byte[] data, string savePath, long jpgQuality)
        {
            try { File.Delete(savePath); } catch { }
            using (MemoryStream ms = new MemoryStream(data))
            using (Bitmap bmp = new Bitmap(ms))
            {
                ImageCodecInfo jpegEncoder = null;
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders()) { if (codec.MimeType == "image/jpeg") { jpegEncoder = codec; break; } }
                if (jpegEncoder != null)
                {
                    System.Drawing.Imaging.Encoder qualityEncoder = System.Drawing.Imaging.Encoder.Quality;
                    EncoderParameters encoderParameters = new EncoderParameters(1);
                    encoderParameters.Param[0] = new EncoderParameter(qualityEncoder, jpgQuality);
                    bmp.Save(savePath, jpegEncoder, encoderParameters);
                }
                else { bmp.Save(savePath, ImageFormat.Jpeg); }
            }

        }
        private static void SaveDirectly(byte[] data, string savePath)
        {
            if (File.Exists(savePath)) File.Delete(savePath);
            File.WriteAllBytes(savePath, data);
        }
    }
}