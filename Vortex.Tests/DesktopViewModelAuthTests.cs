using Vortex.Desktop.Services;
using Vortex.Desktop.ViewModels;
using Vortex.Shared;

namespace Vortex.Tests;

public sealed class DesktopViewModelAuthTests
{
    [Fact]
    public async Task LoginCommand_CompletesAndSwitchesWelcomeToMainScreen()
    {
        var backend = new BackendClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:59999") }, new TokenStorageService());
        var auth = new FakeDesktopAuthenticationService(new UserProfileDto(Guid.NewGuid(), "vm@vortex.local", "VM User", VortexRoles.User, "Ücretsiz Plan", 1024, 0));
        var vm = new MainWindowViewModel(backend, auth);

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.True(vm.IsAuthenticated);
        Assert.False(vm.IsWelcomeVisible);
        Assert.Equal("VM User / Ücretsiz Plan", vm.CurrentUserText);
        Assert.Contains("Hermes", vm.HermesStatusText);
    }

    private sealed class FakeDesktopAuthenticationService(UserProfileDto user) : IDesktopAuthenticationService
    {
        public Task<UserProfileDto?> SignInWithBrowserAsync(bool preferRegister, CancellationToken cancellationToken)
            => Task.FromResult<UserProfileDto?>(user);
    }
}
