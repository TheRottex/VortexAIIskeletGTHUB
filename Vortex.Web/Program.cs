var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient("vortex-server", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Vortex:ServerBaseUrl"] ?? "http://127.0.0.1:5000");
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Vortex.Web" }));
app.Run();
