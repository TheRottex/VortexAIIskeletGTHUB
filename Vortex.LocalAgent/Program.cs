using System.Runtime.InteropServices;
using Vortex.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["LocalAgent:Url"] ?? "http://127.0.0.1:47891");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var app = builder.Build();

var tools = new List<LocalToolDescriptor>
{
    new("read-selected-file", "Kullanıcının seçtiği güvenli metin dosyasını okur.", true, false, LocalToolRiskLevel.Low),
    new("open-program-request", "Program açma isteğini doğrular fakat otomatik çalıştırmaz.", true, true, LocalToolRiskLevel.High),
    new("speak-preview", "TTS entegrasyonu için güvenli önizleme yanıtı döndürür.", true, false, LocalToolRiskLevel.Low)
};

app.MapGet("/health", () => Results.Ok(new LocalAgentHello("Vortex Local Agent", "0.1.0", RuntimeInformation.OSDescription, tools)));
app.MapGet("/api/audio/devices", () => Results.Ok(new[] { new AudioDeviceDto("default", "Varsayılan sistem cihazı", true, true) }));

app.MapPost("/api/tools/invoke", async (LocalToolRequest request, CancellationToken ct) =>
{
    if (request.ExpiresAt < DateTimeOffset.UtcNow) return Results.BadRequest(new LocalToolResponse(request.RequestId, false, "İstek süresi geçmiş."));
    var descriptor = tools.FirstOrDefault(t => t.Name == request.ToolName && t.IsEnabled);
    if (descriptor is null) return Results.BadRequest(new LocalToolResponse(request.RequestId, false, "Araç kayıtlı veya etkin değil."));
    if (descriptor.RequiresConfirmation && !request.UserConfirmed) return Results.BadRequest(new LocalToolResponse(request.RequestId, false, "Bu işlem kullanıcı onayı gerektirir."));

    if (request.ToolName == "speak-preview")
    {
        var text = request.Arguments.TryGetValue("text", out var value) ? value : string.Empty;
        return Results.Ok(new LocalToolResponse(request.RequestId, true, "TTS önizleme hazırlandı.", text.Length > 300 ? text[..300] : text));
    }

    if (request.ToolName == "read-selected-file")
    {
        if (!request.Arguments.TryGetValue("path", out var path) || !File.Exists(path)) return Results.BadRequest(new LocalToolResponse(request.RequestId, false, "Dosya bulunamadı."));
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!SupportedFileTypes.Extensions.Contains(ext)) return Results.BadRequest(new LocalToolResponse(request.RequestId, false, "Desteklenmeyen dosya türü."));
        var info = new FileInfo(path);
        if (info.Length > 2 * 1024 * 1024) return Results.BadRequest(new LocalToolResponse(request.RequestId, false, "Dosya boyutu ilk sürüm sınırını aşıyor."));
        return Results.Ok(new LocalToolResponse(request.RequestId, true, "Dosya okundu.", await File.ReadAllTextAsync(path, ct)));
    }

    if (request.ToolName == "open-program-request")
    {
        return Results.Ok(new LocalToolResponse(request.RequestId, false, "Güvenlik nedeniyle ilk sürümde program otomatik çalıştırılmaz. Desktop açık kullanıcı onayıyla platform kabuğuna devredebilir."));
    }

    return Results.BadRequest(new LocalToolResponse(request.RequestId, false, "Araç uygulanmadı."));
});

app.MapPost("/api/stt/transcribe", (SpeechToTextRequest request) => Results.Ok(new SpeechToTextResponse(string.Empty, 0)));
app.MapPost("/api/tts/speak", (TextToSpeechRequest request) => Results.Ok(new TextToSpeechResponse(true, "TTS servis arayüzü hazır; platform motoru sonraki aşamada bağlanacak.")));

app.Run();
