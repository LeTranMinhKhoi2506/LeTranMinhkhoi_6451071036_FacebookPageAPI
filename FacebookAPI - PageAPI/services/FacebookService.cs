using System.Text.Json;

namespace FacebookAPI___PageAPI.services
{
    public class FacebookService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly string _accessToken;
        private readonly string _graphVersion;

        public FacebookService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;

            _accessToken = _configuration["Facebook:PageAccessToken"] ?? "";
            _graphVersion = _configuration["Facebook:GraphVersion"] ?? "v25.0";
        }

        private string BaseUrl => $"https://graph.facebook.com/{_graphVersion}";

        public async Task<object> GetPageInfoAsync(string pageId)
        {
            var url = $"{BaseUrl}/{pageId}?fields=id,name,link,fan_count,followers_count,about,category&access_token={_accessToken}";
            return await SendGetRequest(url);
        }

        public async Task<object> GetPagePostsAsync(string pageId)
        {
            var url = $"{BaseUrl}/{pageId}/posts?fields=id,message,created_time,permalink_url,full_picture&access_token={_accessToken}";
            return await SendGetRequest(url);
        }

        public async Task<object> CreatePostAsync(string pageId, string message)
        {
            var url = $"{BaseUrl}/{pageId}/feed";

            var formData = new Dictionary<string, string>
            {
                { "message", message },
                { "access_token", _accessToken }
            };

            var content = new FormUrlEncodedContent(formData);
            var response = await _httpClient.PostAsync(url, content);
            return await HandleResponse(response);
        }

        public async Task<object> DeletePostAsync(string postId)
        {
            var url = $"{BaseUrl}/{postId}?access_token={_accessToken}";
            var response = await _httpClient.DeleteAsync(url);
            return await HandleResponse(response);
        }

        public async Task<object> GetPostCommentsAsync(string postId)
        {
            var url = $"{BaseUrl}/{postId}/comments?fields=id,message,from,created_time&access_token={_accessToken}";
            return await SendGetRequest(url);
        }

        public async Task<object> GetPostLikesAsync(string postId)
        {
            var url = $"{BaseUrl}/{postId}/likes?access_token={_accessToken}";
            return await SendGetRequest(url);
        }

        public async Task<object> GetPageInsightsAsync(string pageId)
        {
            var metrics = "page_impressions,page_reach,page_post_engagements,page_fans,page_views";
            var url = $"{BaseUrl}/{pageId}/insights?metric={metrics}&access_token={_accessToken}";
            return await SendGetRequest(url);
        }

        private async Task<object> SendGetRequest(string url)
        {
            var response = await _httpClient.GetAsync(url);
            return await HandleResponse(response);
        }

        private async Task<object> HandleResponse(HttpResponseMessage response)
        {
            var json = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<object>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return data!;
            }

            throw new Exception(json);
        }
    }
}
