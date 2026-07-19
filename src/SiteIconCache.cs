using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProjectorDash
{
    /// <summary>
    /// Tiny persistent favicon cache. It only requests /favicon.ico from the
    /// shortcut's own origin, never blocks startup, and silently keeps the
    /// letter badge when a site has no directly accessible icon.
    /// </summary>
    public static class SiteIconCache
    {
        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = 5000;
                return request;
            }
        }

        private static readonly object Gate = new object();
        private static readonly HashSet<string> Pending =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource TryLoad(string website)
        {
            string path = CachePath(website);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 48;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch
            {
                try { File.Delete(path); } catch { }
                return null;
            }
        }

        public static void EnsureCached(string website, Action completed)
        {
            Uri site;
            if (!Uri.TryCreate(website, UriKind.Absolute, out site)) return;
            if (site.Scheme != Uri.UriSchemeHttp && site.Scheme != Uri.UriSchemeHttps)
                return;
            string path = CachePath(website);
            if (string.IsNullOrEmpty(path) || File.Exists(path)) return;

            lock (Gate)
            {
                if (Pending.Contains(path)) return;
                Pending.Add(path);
            }

            try
            {
                Uri favicon = new Uri(site.GetLeftPart(UriPartial.Authority) + "/favicon.ico");
                TimeoutWebClient client = new TimeoutWebClient();
                client.Headers[HttpRequestHeader.UserAgent] = "ProjectorDashboard/1.0";
                client.DownloadDataCompleted += delegate(object sender,
                    DownloadDataCompletedEventArgs e)
                {
                    bool saved = false;
                    try
                    {
                        if (!e.Cancelled && e.Error == null && e.Result != null &&
                            e.Result.Length > 0 && e.Result.Length <= 1024 * 1024 &&
                            IsDecodableImage(e.Result))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            File.WriteAllBytes(path, e.Result);
                            saved = true;
                        }
                    }
                    catch { }
                    finally
                    {
                        lock (Gate) { Pending.Remove(path); }
                        client.Dispose();
                    }
                    if (saved && completed != null) completed();
                };
                client.DownloadDataAsync(favicon);
            }
            catch
            {
                lock (Gate) { Pending.Remove(path); }
            }
        }

        private static bool IsDecodableImage(byte[] data)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(data, false))
                {
                    BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    return true;
                }
            }
            catch { return false; }
        }

        private static string CachePath(string website)
        {
            try
            {
                Uri uri = new Uri(website);
                string key = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
                byte[] digest;
                using (SHA1 sha = SHA1.Create())
                    digest = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                StringBuilder name = new StringBuilder();
                foreach (byte b in digest) name.Append(b.ToString("x2"));
                return Path.Combine(AppConfig.ConfigDir(), "icons", name + ".img");
            }
            catch { return ""; }
        }
    }
}
