using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vortex.Desktop.Services;

namespace Vortex.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly BackendClient _backendClient;

    [ObservableProperty]
    private string inputText = string.Empty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string statusText = "Backend bağlantısı bekleniyor";

    [ObservableProperty]
    private string activeModel = "Vortex Demo Model";

    public ObservableCollection<MessageViewModel> Messages { get; } = new()
    {
        new("Vortex", "Merhaba. Bu ilk dağıtık Vortex istemcisidir. Backend çalışıyorsa Bağlan düğmesine basıp streaming sohbeti test edebilirsin.")
    };

    public MainWindowViewModel(BackendClient backendClient)
    {
        _backendClient = backendClient;
    }

    [RelayCommand]
    private void NewChat()
    {
        Messages.Clear();
        Messages.Add(new MessageViewModel("Vortex", "Yeni sohbet hazır."));
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        StatusText = "Backend'e bağlanılıyor...";
        var auth = await _backendClient.RegisterOrLoginDevelopmentUserAsync(CancellationToken.None);
        StatusText = auth is null ? "Backend bağlantısı başarısız" : $"{auth.User.DisplayName} / {auth.User.PlanName}";
    }

    [RelayCommand]
    private async Task SendAsync()
    {
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
    [ObservableProperty]
    private string role;

    [ObservableProperty]
    private string content;

    public MessageViewModel(string role, string content)
    {
        this.role = role;
        this.content = content;
    }
}
