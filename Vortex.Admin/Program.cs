using Vortex.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Admin:Url"] ?? "http://127.0.0.1:47892");
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Vortex.Admin" }));
app.MapGet("/", () => Results.Content("""
<!doctype html><html lang="tr"><head><meta charset="utf-8"><title>Vortex Admin</title>
<style>body{margin:0;background:#15172E;color:#EEF2FF;font-family:Inter,Segoe UI,sans-serif}.shell{max-width:960px;margin:48px auto;padding:32px;background:#101213;border-radius:20px}code{color:#8BE9FD}</style></head>
<body><main class="shell"><h1>Vortex Admin</h1><p>Bu panel ayrı process olarak çalışır. Rol tabanlı yönetim API'leri Vortex.Server üzerinden korunur.</p><ul><li>Plan politikaları</li><li>Model yönlendirme</li><li>Sağlayıcı sağlık durumu</li><li>Kullanım ve maliyet raporu</li></ul><p>Sunucu: <code>Vortex.Server /api/admin/*</code></p></main></body></html>
""", "text/html"));

app.MapGet("/api/admin/capabilities", () => Results.Ok(new
{
    roles = VortexRoles.All,
    managed = new[] { "plans", "providers", "models", "quotas", "feature-entitlements", "usage", "audit" },
    note = "Secret values are write-only and masked after saving."
}));

app.Run();
