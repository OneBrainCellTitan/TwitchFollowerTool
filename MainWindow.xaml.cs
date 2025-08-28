using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Navigation;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Newtonsoft.Json;
using TwitchLib.Api;
using TwitchUser = TwitchLib.Api.Helix.Models.Users.GetUsers.User;
using TwitchChannel = TwitchLib.Api.Helix.Models.Channels.GetChannelInformation.ChannelInformation;
using ToolsApi = tools2807.tools2807;
using tools2807.Types;

namespace TwitchFollowerTool_WPF
{

    public class FollowerViewModel : INotifyPropertyChanged
    {
        private string _status = "Очікує аналізу...";
        private int _badCount;
        private int _warningCount;


        private bool _isBlockButtonEnabled = true;
        public bool IsBlockButtonEnabled
        {
            get => _isBlockButtonEnabled;
            set { _isBlockButtonEnabled = value; OnPropertyChanged(); }
        }

        public FollowerInfo Follower { get; }
        public string UserName => Follower.UserName;
        public ObservableCollection<AnalysisDetail> Details { get; set; } = new();
        public int TotalFollows { get; set; }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public int BadCount
        {
            get => _badCount;
            set { _badCount = value; OnPropertyChanged(); UpdateStatus(); }
        }

        public int WarningCount
        {
            get => _warningCount;
            set { _warningCount = value; OnPropertyChanged(); UpdateStatus(); }
        }

        public FollowerViewModel(FollowerInfo follower)
        {
            Follower = follower;
        }

        private void UpdateStatus()
        {
            if (TotalFollows > 0)
            {
                if (BadCount > 0 || WarningCount > 0)
                {
                    Status = $"🟥 Болотних: {BadCount}, 🟨 Підозрілих: {WarningCount} (з {TotalFollows})";
                }
                else
                {
                    Status = $"✅ Не виявлено (з {TotalFollows})";
                }
            }
            else if (Status == "Немає підписок")
            {
                
            }
            else
            {
                Status = "✅ Не виявлено";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }


    public class AnalysisDetail
    {
        public string Icon { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string FollowDate { get; set; } = "";
        public string Reason { get; set; } = "";
    }



    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private TwitchAPI? _twitchApi;
        private string _broadcasterId = "";
        private readonly ToolsApi _toolsApi = new ToolsApi();
        private string _streamerName = "";
        private string _streamerAvatarUrl = "";
        private bool _isBusy = false;


        public ObservableCollection<FollowerViewModel> Followers { get; set; }
        public string StreamerName
        {
            get => _streamerName;
            set { _streamerName = value; OnPropertyChanged(); }
        }
        public string StreamerAvatarUrl
        {
            get => _streamerAvatarUrl;
            set { _streamerAvatarUrl = value; OnPropertyChanged(); }
        }

        public MainWindow()
        {
            InitializeComponent();
            Followers = new ObservableCollection<FollowerViewModel>();
            this.DataContext = this;
            SetTheme(true);
            Loaded += async (s, e) => await UpdateChecker.CheckForUpdatesAsync();
        }


        private void SetBusyState(bool isBusy)
        {
            _isBusy = isBusy;


            GetFollowersButton.IsEnabled = !isBusy;
            AnalyzeAllButton.IsEnabled = !isBusy;
            LogoutButton.IsEnabled = !isBusy;

            foreach (var followerVM in Followers.ToList())
            {
                followerVM.IsBlockButtonEnabled = !isBusy;
            }
        }


        private void SupportLink_Click(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося відкрити посилання: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }

        private void SetTheme(bool isDark)
        {
            var app = Application.Current;
            app.Resources.MergedDictionaries.Clear();
            var resourceDict = new ResourceDictionary();
            if (isDark)
            {
                resourceDict.Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);
            }
            else
            {
                resourceDict.Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
            }
            app.Resources.MergedDictionaries.Add(resourceDict);
        }

        private void ThemeToggleButton_Checked(object sender, RoutedEventArgs e) => SetTheme(true);
        private void ThemeToggleButton_Unchecked(object sender, RoutedEventArgs e) => SetTheme(false);

        private async void AuthorizeButton_Click(object sender, RoutedEventArgs e)
        {
            var authButton = (Button)sender;
            authButton.IsEnabled = false;
            authButton.Content = "Авторизація...";

            string? accessToken = await TwitchAuthService.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                MessageBox.Show("Авторизація не вдалася.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                authButton.IsEnabled = true;
                authButton.Content = "Увійти через Twitch";
                return;
            }

            _twitchApi = new TwitchAPI();
            _twitchApi.Settings.ClientId = TwitchAuthService.ClientId;
            _twitchApi.Settings.AccessToken = accessToken;

            var usersResponse = await _twitchApi.Helix.Users.GetUsersAsync();
            if (usersResponse == null || usersResponse.Users.Length == 0)
            {
                MessageBox.Show("Не вдалося отримати інформацію про стрімера.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                authButton.IsEnabled = true;
                authButton.Content = "Увійти через Twitch";
                return;
            }

            TwitchUser streamer = usersResponse.Users[0];
            _broadcasterId = streamer.Id;
            StreamerName = streamer.DisplayName;
            StreamerAvatarUrl = streamer.ProfileImageUrl;

            authButton.Visibility = Visibility.Collapsed;
            MainGrid.Visibility = Visibility.Visible;
            Title = $"Twitch Follower Manager - {streamer.DisplayName}";
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _twitchApi = null;
            _broadcasterId = "";
            StreamerName = "";
            StreamerAvatarUrl = "";
            Followers.Clear();
            StatusText.Text = "Готово до роботи.";

            MainGrid.Visibility = Visibility.Collapsed;
            AuthorizeButton.Visibility = Visibility.Visible;
            AuthorizeButton.IsEnabled = true;
            AuthorizeButton.Content = "Увійти через Twitch";
            Title = "Twitch Follower Manager";
            SetTheme(true);
        }

        private async void GetFollowersButton_Click(object sender, RoutedEventArgs e)
        {
            if (_twitchApi == null || _isBusy) return;

            SetBusyState(true); 
            StatusText.Text = "Отримання списку фоловерів...";

            try
            {
                Followers.Clear();
                var followersList = await GetMyFollowersAsync(_twitchApi, _broadcasterId);

                foreach (var follower in followersList)
                {
                    Followers.Add(new FollowerViewModel(follower));
                }

                StatusText.Text = $"Готово. Отримано {Followers.Count} фоловерів.";
            }
            finally
            {
                SetBusyState(false);
            }
        }

        
        private async void AnalyzeAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_twitchApi == null || Followers.Count == 0 || _isBusy) return;

            SetBusyState(true);
            StatusText.Text = "Запуск повного аналізу...";

            try
            {
                int processedCount = 0;
                foreach (var followerVM in Followers.ToList())
                {
                    processedCount++;
                    StatusText.Text = $"Аналіз... {processedCount}/{Followers.Count} ({followerVM.UserName})";
                    await AnalyzeFollower(followerVM);
                }
                StatusText.Text = "Аналіз завершено.";
            }
            finally
            {
                SetBusyState(false); 
            }
        }


        private async void BlockButton_Click(object sender, RoutedEventArgs e)
        {
            if (_twitchApi == null || _isBusy || sender is not Button blockButton || blockButton.DataContext is not FollowerViewModel selectedFollower)
            {
                return;
            }

            var result = MessageBox.Show($"Ви впевнені, що хочете заблокувати {selectedFollower.UserName}?", "Підтвердження", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _twitchApi.Helix.Users.BlockUserAsync(selectedFollower.Follower.UserId);
                    MessageBox.Show($"Користувач {selectedFollower.UserName} успішно заблокований.", "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                    Followers.Remove(selectedFollower);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Помилка блокування: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task AnalyzeFollower(FollowerViewModel followerVM)
        {
            if (_twitchApi == null) return;

            if (followerVM.TotalFollows > 0 && followerVM.Status != "Очікує аналізу...")
            {
                return;
            }

            StatusText.Text = $"Отримання деталей для {followerVM.UserName}...";

            try
            {
                FollowUser[]? follows = await _toolsApi.GetFollows(followerVM.UserName);
                followerVM.TotalFollows = follows?.Length ?? 0;

                if (follows == null || !follows.Any())
                {
                    followerVM.Status = "Немає підписок";
                    return;
                }

                var followsDictionary = follows
                    .GroupBy(f => f.id)
                    .ToDictionary(g => g.Key, g => g.First());

                var channelIds = followsDictionary.Keys.ToList();
                var channelsInfo = await GetChannelsInfoAsync(_twitchApi, channelIds);

                int badCount = 0;
                int warningCount = 0;

                followerVM.Details.Clear();
                foreach (var channel in channelsInfo)
                {
                    var analysis = ChannelAnalyzer.Analyze(channel);
                    if (analysis.Status == ChannelStatus.Bad || analysis.Status == ChannelStatus.Warning)
                    {
                        var followInfo = followsDictionary[channel.BroadcasterId];
                        followerVM.Details.Add(new AnalysisDetail
                        {
                            Icon = analysis.Status == ChannelStatus.Bad ? "🟥" : "🟨",
                            ChannelName = channel.BroadcasterName,
                            FollowDate = followInfo.followedAt?.ToString("dd MMMM yyyy р.") ?? "Невідома дата",
                            Reason = string.Join(", ", analysis.Reasons)
                        });
                        if (analysis.Status == ChannelStatus.Bad) badCount++;
                        else if (analysis.Status == ChannelStatus.Warning) warningCount++;
                    }
                }
                if (!followerVM.Details.Any())
                {
                    followerVM.Details.Add(new AnalysisDetail { ChannelName = "Проблемних підписок не виявлено." });
                }

                followerVM.BadCount = badCount;
                followerVM.WarningCount = warningCount;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка отримання деталей: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StatusText.Text = "Готово.";
            }
        }

        private void FollowersGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (FollowersGrid.SelectedItem is FollowerViewModel selectedFollower)
            {
                AnalyzeFollower(selectedFollower);
            }
        }

        private void FollowersGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column.SortMemberPath == "BadCount")
            {
                e.Handled = true;

                ListSortDirection direction = e.Column.SortDirection != ListSortDirection.Ascending ?
                    ListSortDirection.Ascending : ListSortDirection.Descending;

                var sortedList = (direction == ListSortDirection.Ascending) ?
                    Followers.OrderBy(f => f.BadCount).ThenBy(f => f.WarningCount).ToList() :
                    Followers.OrderByDescending(f => f.BadCount).ThenByDescending(f => f.WarningCount).ToList();

                Followers.Clear();
                foreach (var item in sortedList)
                {
                    Followers.Add(item);
                }

                e.Column.SortDirection = direction;
            }
        }

        private async Task<List<FollowerInfo>> GetMyFollowersAsync(TwitchAPI api, string broadcasterId)
        {
            var allFollowers = new List<FollowerInfo>();
            string? cursor = null;
            using var httpClient = new HttpClient();
            try
            {
                while (true)
                {
                    string url = $"https://api.twitch.tv/helix/channels/followers?broadcaster_id={broadcasterId}&first=100";
                    if (!string.IsNullOrEmpty(cursor)) url += $"&after={cursor}";

                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Client-ID", api.Settings.ClientId);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Settings.AccessToken);

                    var response = await httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        MessageBox.Show($"Помилка API: {await response.Content.ReadAsStringAsync()}");
                        break;
                    }

                    string jsonString = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<GetFollowersResponse>(jsonString);

                    if (result == null || result.Data == null || result.Data.Count == 0) break;

                    allFollowers.AddRange(result.Data);
                    cursor = result.Pagination?.Cursor;

                    if (string.IsNullOrEmpty(cursor)) break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критична помилка отримання фоловерів: {ex.Message}");
            }
            return allFollowers;
        }

        private async Task<List<TwitchChannel>> GetChannelsInfoAsync(TwitchAPI api, List<string> channelIds)
        {
            using var httpClient = new HttpClient();
            var channelsInfo = new List<TwitchChannel>();

            foreach (var chunk in channelIds.Chunk(100))
            {
                var idParams = string.Join("&", chunk.Select(id => $"broadcaster_id={id}"));
                string url = $"https://api.twitch.tv/helix/channels?{idParams}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Client-ID", api.Settings.ClientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Settings.AccessToken);

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<GetChannelsResponse>(jsonString);

                    if (result != null && result.Data != null)
                    {
                        channelsInfo.AddRange(result.Data);
                    }
                }
            }
            return channelsInfo;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class GetFollowersResponse { [JsonProperty("data")] public List<FollowerInfo> Data { get; set; } = new(); [JsonProperty("pagination")] public Pagination Pagination { get; set; } = new(); }
    public class FollowerInfo { [JsonProperty("user_name")] public string UserName { get; set; } = ""; [JsonProperty("user_id")] public string UserId { get; set; } = ""; }
    public class Pagination { [JsonProperty("cursor")] public string? Cursor { get; set; } }
    public class GetChannelsResponse { [JsonProperty("data")] public List<TwitchChannel> Data { get; set; } = new(); }

    public static class TwitchAuthService
    {
        public const string ClientId = "4u90ix62rwav0debvu1qp2ldixu9ml";
        private const string RedirectUrl = "http://localhost:3000/";

        public static async Task<string?> GetAccessTokenAsync()
        {
            var scopes = "user:read:email moderator:read:followers user:manage:blocked_users";
            string authUrl = $"https://id.twitch.tv/oauth2/authorize?response_type=token&client_id={ClientId}&redirect_uri={Uri.EscapeDataString(RedirectUrl)}&scope={Uri.EscapeDataString(scopes)}";

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            try
            {
                using var listener = new HttpListener();
                listener.Prefixes.Add(RedirectUrl);
                listener.Start();

                var context = await listener.GetContextAsync();
                var response = context.Response;

                response.ContentType = "text/html; charset=utf-8";
                string responseString = @"
                    <html><head><meta charset=""UTF-8""></head>
                    <body><script>
                        let hash = window.location.hash.substring(1);
                        let params = new URLSearchParams(hash);
                        let token = params.get('access_token');
                        fetch('/?token=' + token).then(() => window.close());
                    </script><p>Авторизація... Можете закрити цю вкладку.</p></body></html>";

                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();

                context = await listener.GetContextAsync();
                return context.Request.QueryString.Get("token");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка авторизації: {ex.Message}");
                return null;
            }
        }
    }

    public enum ChannelStatus { Neutral, Friend, Warning, Bad }
    public class AnalysisResult
    {
        public ChannelStatus Status { get; set; } = ChannelStatus.Neutral;
        public List<string> Reasons { get; set; } = new();
    }

    public static class ChannelAnalyzer
    {
        private static readonly List<string> BadTags = new() { "русский", "россия", "russian" };
        private static readonly List<string> FriendTags = new() { "українською", "ukraine", "україна", "ukrainian" };

        private static bool ContainsUkrainianChars(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOfAny("іІїЇєЄґҐ".ToCharArray()) >= 0;
        }

        public static AnalysisResult Analyze(TwitchChannel channel)
        {
            var result = new AnalysisResult();
            var tags = channel.Tags.Select(t => t.ToLower()).ToList();
            var badReasons = new List<string>();
            var friendReasons = new List<string>();

            if (channel.BroadcasterLanguage == "ru") badReasons.Add("Мова стріму - російська");
            if (tags.Any(t => BadTags.Contains(t))) badReasons.Add("Має болотний тег");

            if (tags.Any(t => FriendTags.Contains(t))) friendReasons.Add("Має український тег");
            if (ContainsUkrainianChars(channel.Title)) friendReasons.Add("Українські символи в назві стріму");

            bool hasBadIndicator = badReasons.Any();
            bool hasFriendIndicator = friendReasons.Any();

            if (hasBadIndicator)
            {
                if (hasFriendIndicator)
                {
                    result.Status = ChannelStatus.Warning;
                    result.Reasons.AddRange(badReasons);
                    result.Reasons.AddRange(friendReasons);
                }
                else
                {
                    result.Status = ChannelStatus.Bad;
                    result.Reasons.AddRange(badReasons);
                }
            }
            else if (hasFriendIndicator)
            {
                result.Status = ChannelStatus.Friend;
            }

            return result;
        }
    }
}
