// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace Prisma
{
    // A shared cloud folder where an assistant (Claude, ChatGPT) files the review notes.
    // Only https links to Google Drive, Dropbox and OneDrive/SharePoint are accepted.
    internal static class CloudFolder
    {
        public const int MaxLength = 2048;

        public static bool TryNormalize(string input, out string url, out string service)
        {
            url = ""; service = "";
            if (string.IsNullOrWhiteSpace(input)) return false;
            string text = input.Trim();
            if (text.Length > MaxLength || text.IndexOfAny(new char[] { ' ', '\t', '\r', '\n', '"', '<', '>' }) >= 0) return false;
            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) text = "https://" + text;
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || !uri.IsDefaultPort) return false;
            service = ServiceOf(uri.IdnHost.ToLowerInvariant());
            if (service.Length == 0) return false;
            url = uri.AbsoluteUri;
            return true;
        }

        public static string ServiceName(string url)
        {
            string normalized, service;
            return TryNormalize(url, out normalized, out service) ? service : "";
        }

        private static string ServiceOf(string host)
        {
            if (host == "drive.google.com" || host == "docs.google.com") return "Google Drive";
            if (host == "dropbox.com" || host == "www.dropbox.com") return "Dropbox";
            if (host == "onedrive.live.com" || host == "1drv.ms" || host == "onedrive.cloud.microsoft") return "OneDrive";
            if (host.EndsWith(".sharepoint.com", StringComparison.Ordinal) && host.Length > ".sharepoint.com".Length) return "OneDrive";
            return "";
        }
    }
}
