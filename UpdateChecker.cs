using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Octokit;

public static class UpdateChecker
{
    private const string GitHubUsername = "OneBrainCellTitan";
    private const string GitHubRepoName = "TwitchFollowerTool";

    public static async Task CheckForUpdatesAsync()
    {
        try
        {
            var client = new GitHubClient(new ProductHeaderValue(GitHubRepoName));
            var latestRelease = await client.Repository.Release.GetLatest(GitHubUsername, GitHubRepoName);

            // Якщо релізів ще немає, нічого не робимо
            if (latestRelease == null) return;

            var latestVersionStr = latestRelease.TagName.TrimStart('v');
            if (!Version.TryParse(latestVersionStr, out var latestVersion))
            {
                return;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (latestVersion > currentVersion)
            {
                var result = MessageBox.Show(
                    $"Доступна нова версія {latestVersionStr}!\n\n" +
                    $"Ваша поточна версія: {currentVersion.ToString(3)}\n" +
                    "Бажаєте перейти на сторінку завантаження?",
                    "Є оновлення!",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {

                    Process.Start(new ProcessStartInfo(latestRelease.HtmlUrl)
                    {
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Помилка перевірки оновлень: {ex.Message}");
        }
    }
}