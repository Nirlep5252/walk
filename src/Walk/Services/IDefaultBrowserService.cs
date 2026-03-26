namespace Walk.Services;

public interface IDefaultBrowserService
{
    string BrowserDisplayName { get; }
    void SearchWeb(string query);
}
