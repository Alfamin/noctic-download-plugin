using System.Globalization;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Noctis.Plugins;

namespace FreeMusicFinder;

/// <summary>
/// Logs the user's own Telegram account in: API id and hash and phone number, then the code
/// Telegram sends, then the two-step password if the account has one. Shows who is logged in
/// and offers to log out when a session already exists.
/// </summary>
internal sealed class TelegramLoginWindow : Window
{
    private enum Step { Phone, Code, Password, LoggedIn }

    private readonly TelegramAccount _account;
    private readonly IPluginHost _host;
    private Step _step;
    private bool _closed;

    private readonly TextBlock _intro;
    private readonly StackPanel _phonePanel;
    private readonly TextBox _apiId;
    private readonly TextBox _apiHash;
    private readonly TextBox _phone;
    private readonly TextBox _code;
    private readonly TextBox _password;
    private readonly TextBlock _error;
    private readonly Button _back;
    private readonly Button _next;

    public TelegramLoginWindow(TelegramAccount account, IPluginHost host)
    {
        _account = account;
        _host = host;

        Title = "Telegram login";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _intro = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _apiId = new TextBox { PlaceholderText = "API id (a number)", Text = account.ApiId > 0 ? account.ApiId.ToString(CultureInfo.InvariantCulture) : "" };
        _apiHash = new TextBox { PlaceholderText = "API hash", Text = account.ApiHash };
        _phone = new TextBox { PlaceholderText = "Phone number with country code (+49…)" };
        _phonePanel = new StackPanel { Spacing = 8 };
        _phonePanel.Children.Add(_apiId);
        _phonePanel.Children.Add(_apiHash);
        _phonePanel.Children.Add(_phone);
        _code = new TextBox { PlaceholderText = "Login code" };
        _password = new TextBox { PlaceholderText = "Two-step verification password", PasswordChar = '•' };
        _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed, IsVisible = false };

        _back = new Button { Margin = new Thickness(0, 0, 8, 0) };
        _back.Click += (_, _) => Back();
        _next = new Button { MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        _next.Click += (_, _) => Next();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);

        var root = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        root.Children.Add(_intro);
        root.Children.Add(_phonePanel);
        root.Children.Add(_code);
        root.Children.Add(_password);
        root.Children.Add(_error);
        root.Children.Add(buttons);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || !_next.IsEnabled) return;
            e.Handled = true;
            Next();
        };
        Closed += (_, _) => _closed = true;

        Show(account.IsLoggedIn ? Step.LoggedIn : Step.Phone);
    }

    private void Show(Step step)
    {
        _step = step;
        _phonePanel.IsVisible = step == Step.Phone;
        _code.IsVisible = step == Step.Code;
        _password.IsVisible = step == Step.Password;
        _back.IsVisible = step != Step.Phone;
        _back.Content = step == Step.LoggedIn ? "Log out" : "Start over";
        _next.Content = step == Step.LoggedIn ? "Close" : "Continue";
        _intro.Text = step switch
        {
            Step.Phone => "The music bot is used through your own Telegram account. Get an API id and hash at "
                + "my.telegram.org → API development tools, then enter them with your phone number. "
                + "Only the session Telegram issues is kept, encrypted for your user account on this computer.",
            Step.Code => "Telegram sent a login code to your Telegram app (or by SMS). Enter it here.",
            Step.Password => "This account has two-step verification. Enter its password; it is not stored.",
            _ => $"Logged in as {_account.SignedInAs}.",
        };
        (step switch { Step.Phone => _apiId.Text is { Length: > 0 } ? _phone : _apiId, Step.Code => _code, Step.Password => _password, _ => (Control)_next }).Focus();
    }

    private async void Next()
    {
        try
        {
            if (_step == Step.LoggedIn)
            {
                Close();
                return;
            }

            _error.IsVisible = false;
            string? wanted;
            SetBusy(true);
            try
            {
                switch (_step)
                {
                    case Step.Phone:
                        var phone = (_phone.Text ?? "").Trim();
                        var hash = (_apiHash.Text ?? "").Trim();
                        if (!int.TryParse((_apiId.Text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || hash.Length == 0 || phone.Length == 0)
                        {
                            Fail("Enter the API id, the API hash and your phone number.");
                            return;
                        }
                        wanted = await _account.StartLoginAsync(id, hash, phone);
                        break;
                    case Step.Code:
                        wanted = await _account.ContinueLoginAsync((_code.Text ?? "").Trim());
                        break;
                    default:
                        wanted = await _account.ContinueLoginAsync(_password.Text ?? "");
                        break;
                }
            }
            finally
            {
                SetBusy(false);
            }
            if (_closed) return;

            _code.Text = "";
            _password.Text = "";
            switch (wanted)
            {
                case null: Show(Step.LoggedIn); break;
                case "verification_code": Show(Step.Code); break;
                case "password": Show(Step.Password); break;
                case "name": Fail("There is no Telegram account for this phone number."); break;
                default: Fail($"Telegram asked for \"{wanted}\", which this window cannot provide."); break;
            }
        }
        catch (Exception ex)
        {
            _host.Log("Telegram login failed: " + ex.Message);
            if (!_closed) Fail(Explain(ex));
        }
    }

    private async void Back()
    {
        try
        {
            _error.IsVisible = false;
            SetBusy(true);
            try { await _account.LogOutAsync(); }
            finally { SetBusy(false); }
            if (!_closed) Show(Step.Phone);
        }
        catch (Exception ex)
        {
            _host.Log("Telegram logout failed: " + ex.Message);
            if (!_closed) Fail(ex.Message);
        }
    }

    private void SetBusy(bool busy)
    {
        _next.IsEnabled = !busy;
        _back.IsEnabled = !busy;
    }

    private void Fail(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    private static string Explain(Exception ex) => ex is SocketException or IOException or TimeoutException
        // Not an answer from Telegram: the connection itself did not work.
        ? "Telegram could not be reached (" + ex.Message.TrimEnd('.') + "). Where Telegram is blocked, turn on a VPN "
          + "or set a proxy in the plugin's settings (Settings → Plugins → Free Music Finder)."
        : Explain(ex.Message);

    private static string Explain(string error) => error switch
    {
        "PHONE_CODE_INVALID" => "That code is not right. Check it and try again.",
        "PHONE_CODE_EXPIRED" => "That code has expired. Start over to get a new one.",
        "PASSWORD_HASH_INVALID" => "That password is not right.",
        "PHONE_NUMBER_INVALID" => "Telegram does not accept that phone number. Include the country code.",
        "API_ID_INVALID" => "Telegram does not accept that API id and hash.",
        _ when error.StartsWith("FLOOD_WAIT", StringComparison.Ordinal) => "Too many attempts. Telegram asks you to wait before trying again.",
        _ => error,
    };
}
