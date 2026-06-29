using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vortex.Desktop.Services;
using Vortex.Shared;

namespace Vortex.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly BackendClient _backendClient;
    private readonly IDesktopAuthenticationService _authenticationService;
    private CancellationTokenSource? _authCancellation;

    [ObservableProperty] private string inputText = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusText = "Giriş yapılmadı";
    [ObservableProperty] private string activeModel = "Vortex Demo Model";
    [ObservableProperty] private bool isAuthenticated;
    [ObservableProperty] private bool isWelcomeVisible = true;
    [ObservableProperty] private bool isAuthenticating;
    [ObservableProperty] private string currentUserText = string.Empty;
    [ObservableProperty] private string hermesStatusText = "Hermes profili bekleniyor";

    public ObservableCollection<MessageViewModel> Messages { get; } = new()
    {
        new("Vortex", "Merhaba. Güvenli web girişi tamamlandıktan sonra sohbet ekranı açılır.")
    };

    public MainWindowViewModel(BackendClient backendClient, IDesktopAuthenticationService authenticationService)
    {
        _backendClient = backendClient;
        _authenticationService = authenticationService;
        _ = LoadStoredSessionAsync();
    }

    partial void OnIsAuthenticatedChanged(bool value)
    {
        IsWelcomeVisible = !value;
    }

    private async Task LoadStoredSessionAsync()
    {
        try
        {
            if (await _backendClient.TryLoadStoredTokenAsync(CancellationToken.None))
            {
                var me = await _backendClient.GetMeAsync(CancellationToken.None);
                if (me is not null) await ApplyAuthenticatedUserAsync(me, CancellationToken.None);
            }
        }
        catch
        {
            StatusText = "Kaydedilmiş oturum okunamadı.";
        }
    }

    [RelayCommand]
    private async Task LoginAsync() => await AuthenticateAsync(preferRegister: false);

    [RelayCommand]
    private async Task RegisterAsync() => await AuthenticateAsync(preferRegister: true);

    [RelayCommand]
    private void CancelLogin()
    {
        _authCancellation?.Cancel();
        IsAuthenticating = false;
        StatusText = "Giriş iptal edildi.";
    }

    [RelayCommand]
    private void Logout()
    {
        _backendClient.Logout();
        IsAuthenticated = false;
        CurrentUserText = string.Empty;
        HermesStatusText = "Hermes profili bekleniyor";
        StatusText = "Oturum kapatıldı.";
    }

    private async Task AuthenticateAsync(bool preferRegister)
    {
        if (IsAuthenticating) return;
        _authCancellation = new CancellationTokenSource();
        IsAuthenticating = true;
        StatusText = preferRegister ? "Kayıt için tarayıcı açılıyor..." : "Giriş için tarayıcı açılıyor...";
        try
        {
            var user = await _authenticationService.SignInWithBrowserAsync(preferRegister, _authCancellation.Token);
            if (user is null)
            {
                StatusText = "Giriş tamamlanamadı.";
                return;
            }
            await ApplyAuthenticatedUserAsync(user, _authCancellation.Token);
        }
        catch (Exception ex)
        {
            DesktopLogService.Error("AuthenticateAsync catch bloğu exception ayrıntısını yakaladı.", ex);
            await RunOnUiThreadAsync(() =>
            {
                StatusText = "Giriş işlemi başarısız: " + ex.Message;
            });
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    private async Task ApplyAuthenticatedUserAsync(
        UserProfileDto user,
        CancellationToken cancellationToken)
    {
        await RunOnUiThreadAsync(() =>
        {
            IsAuthenticated = true;
            IsWelcomeVisible = false;
            CurrentUserText = $"{user.DisplayName} / {user.PlanName}";
            StatusText = CurrentUserText;
            HermesStatusText = "Hermes profili kontrol ediliyor...";
        });

        DesktopLogService.Info("9. Avalonia ViewModel giriş durumuna geçirildi.");
        DesktopLogService.Info($"10. Karşılama ekranı kapandı ve ana sohbet ekranı açıldı. IsAuthenticated={IsAuthenticated}, IsWelcomeVisible={IsWelcomeVisible}.");

        try
        {
            var agent = await _backendClient.GetAgentStatusAsync(cancellationToken);

            await RunOnUiThreadAsync(() =>
            {
                HermesStatusText = agent?.Profile is null
                    ? "Hermes profili yok"
                    : $"Hermes: {agent.Profile.Status} / Kalan agent hakkı: {agent.RemainingRunsToday}";
            });
        }
        catch
        {
            await RunOnUiThreadAsync(() =>
            {
                HermesStatusText = "Hermes durumu alınamadı";
            });
        }
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        if (Application.Current is null || Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action);
    }

    [RelayCommand]
    private void NewChat()
    {
        Messages.Clear();
        Messages.Add(new MessageViewModel("Vortex", "Yeni sohbet hazır."));
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (!IsAuthenticated)
        {
            StatusText = "Önce web üzerinden giriş yapın.";
            return;
        }
        var text = InputText.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        InputText = string.Empty;
        Messages.Add(new MessageViewModel("Kullanıcı", text));
        var assistant = new MessageViewModel("Asistan", string.Empty);
        Messages.Add(assistant);
        try
        {
            await foreach (var chunk in _backendClient.StreamChatAsync(text, CancellationToken.None))
            {
                if (!string.IsNullOrEmpty(chunk.ModelName)) ActiveModel = chunk.ModelName;
                assistant.Content += chunk.Delta;
            }
        }
        catch (Exception ex)
        {
            assistant.Content = "Backend streaming yanıtı alınamadı: " + ex.Message;
        }
    }

    [RelayCommand]
    private void PushToTalk()
    {
        Messages.Add(new MessageViewModel("Ses", "Bas-konuş arayüzü hazır. Local Agent STT sağlayıcısı sonraki aşamada bağlanacak."));
    }
}

public sealed partial class MessageViewModel : ObservableObject
{
    [ObservableProperty] private string role;
    [ObservableProperty] private string content;

    public MessageViewModel(string role, string content)
    {
        this.role = role;
        this.content = content;
    }
}
