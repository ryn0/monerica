﻿using System.Text.RegularExpressions;

namespace DirectoryManager.Web.Helpers
{
    public class UrlHelper
    {
        private static readonly Regex TorRegex =
            new Regex(
                @"^http[s]?://[a-z2-7]{16}\.onion(/.*)?$",
                RegexOptions.Compiled |
                RegexOptions.IgnoreCase);

        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            // Check if it's a valid .onion link
            if (TorRegex.IsMatch(url))
            {
                return true;
            }

            // Check if it's a valid regular URL
            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        public static string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("URL cannot be null or empty.", nameof(url));
            }

            // Remove multiple slashes (except for the protocol part)
            string normalizedUrl = Regex.Replace(url, "(?<!:)/{2,}", "/");

            // Remove the ending slash if present
            if (normalizedUrl.EndsWith("/"))
            {
                normalizedUrl = normalizedUrl.TrimEnd('/');
            }

            return normalizedUrl;
        }

        public static string MakeFullUrl(string domain, string relativePath)
        {
            // ensure the domain has no trailing slash before building
            var baseUri = new Uri(domain.TrimEnd('/'), UriKind.Absolute);

            // combine domain + path
            var fullUri = new Uri(baseUri, relativePath);

            // AbsoluteUri gives you the normalized URL, then TrimEnd removes any trailing slash
            return fullUri.AbsoluteUri.TrimEnd('/');
        }
    }
}