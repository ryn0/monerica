﻿namespace DirectoryManager.Console.Helpers
{
    public class WebPageChecker
    {
        private const string DefaultHeader = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36";

        public static async Task<bool> IsWebPageOnlineAsync(Uri uri)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultHeader);

            try
            {
                var response = await client.GetAsync(uri);

                if (response.IsSuccessStatusCode || ((int)response.StatusCode > 400))
                {
                    return true;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Moved ||
                         response.RequestMessage?.RequestUri != uri)
                {
                    return false;
                }
                else
                {
                    return false;
                }
            }
            catch (HttpRequestException)
            {
                // Handle the request exception, e.g., log it if needed.
                return false;
            }
            catch (TaskCanceledException)
            {
                // Handle the task cancellation, e.g., log it if needed.
                return false;
            }
        }

        public static bool IsWebPageOnlinePing(Uri uri)
        {
            try
            {
                var ping = new System.Net.NetworkInformation.Ping();

                var result = ping.Send(uri.Host);

                return result.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}