using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OrbitRender
{
    /// <summary>GitHub release lookup and verified asset download.</summary>
    internal static class UpdateTransport
    {
        private const string Repository = "imnyang/OrbitRender";
        private const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        internal const long MaximumDownloadBytes = 512L * 1024 * 1024;
        private const int TimeoutMs = 20000;

        /// <summary>Returns the latest stable release, or null when none is published.</summary>
        internal static UpdateRelease GetLatestRelease()
        {
            string json;
            try
            {
                using (var response = Send(LatestReleaseUrl, "application/vnd.github+json"))
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.NotFound) return null;
                if (response != null && response.StatusCode == HttpStatusCode.Forbidden)
                    throw new InvalidOperationException("GitHub API rate limit reached; will retry next launch.", ex);
                throw;
            }

            var release = JObject.Parse(json);
            // /releases/latest already hides drafts and pre-releases; keep the
            // check explicit so a proxy or API change can't push a beta build.
            if (release.Value<bool?>("draft") == true || release.Value<bool?>("prerelease") == true) return null;

            var tag = release.Value<string>("tag_name");
            Version version;
            if (!UpdateVersion.TryParse(tag, out version))
                throw new InvalidDataException("Release tag is not a supported version: " + tag);

            var asset = FindAsset(release["assets"] as JArray);
            if (asset == null) throw new InvalidDataException("Release " + tag + " has no OrbitRender ZIP asset.");

            var url = asset.Value<string>("browser_download_url");
            if (!IsTrustedUrl(url)) throw new InvalidDataException("Release asset URL is not trusted.");

            var size = asset.Value<long?>("size") ?? -1;
            if (size > MaximumDownloadBytes) throw new InvalidDataException("Release asset exceeds the update size limit.");

            string sha256 = null;
            var digest = asset.Value<string>("digest");
            if (!string.IsNullOrEmpty(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha256 = digest.Substring(7).Trim().ToLowerInvariant();

            return new UpdateRelease
            {
                Tag = tag,
                Version = version,
                AssetName = asset.Value<string>("name"),
                DownloadUrl = url,
                Size = size,
                Sha256 = sha256
            };
        }

        /// <summary>Downloads the asset to <paramref name="destination"/> and verifies size and SHA-256.</summary>
        internal static void Download(UpdateRelease release, string destination, Action<int> progress)
        {
            using (var response = Send(release.DownloadUrl, "application/octet-stream"))
            {
                // Redirects must stay on GitHub's own hosts.
                if (!IsTrustedUrl(response.ResponseUri.AbsoluteUri))
                    throw new InvalidDataException("Update download was redirected to an untrusted host.");

                var expected = release.Size > 0 ? release.Size : response.ContentLength;
                using (var input = response.GetResponseStream())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[64 * 1024];
                    long total = 0;
                    var lastPercent = -1;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > MaximumDownloadBytes) throw new InvalidDataException("Downloaded update is too large.");
                        output.Write(buffer, 0, read);
                        if (expected > 0 && progress != null)
                        {
                            var percent = (int)Math.Min(99L, total * 100L / expected);
                            if (percent != lastPercent) { lastPercent = percent; progress(percent); }
                        }
                    }
                    if (expected > 0 && total != expected)
                        throw new InvalidDataException("Downloaded update size does not match the release metadata.");
                }
            }

            if (release.Sha256 != null)
            {
                string actual;
                using (var sha = SHA256.Create())
                using (var input = File.OpenRead(destination))
                    actual = ToHex(sha.ComputeHash(input));
                if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded update SHA-256 does not match the release digest.");
            }
        }

        private static JObject FindAsset(JArray assets)
        {
            if (assets == null) return null;
            JObject fallback = null;
            foreach (var token in assets)
            {
                var asset = token as JObject;
                var name = asset == null ? null : asset.Value<string>("name");
                if (name == null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, "OrbitRender.zip", StringComparison.OrdinalIgnoreCase)) return asset;
                if (fallback == null && name.StartsWith("OrbitRender", StringComparison.OrdinalIgnoreCase)) fallback = asset;
            }
            return fallback;
        }

        private static bool IsTrustedUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
            var host = uri.Host.ToLowerInvariant();
            return host == "github.com" || host == "api.github.com"
                || host.EndsWith(".github.com", StringComparison.Ordinal)
                || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
        }

        private static HttpWebResponse Send(string url, string accept)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "OrbitRender-Updater/2.0";
            request.Accept = accept;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = true;
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            return (HttpWebResponse)request.GetResponse();
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
