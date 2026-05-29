using System.Net;
using System.Net.Http.Json;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;

namespace BattleGrid.Tests.Integration;

public sealed class AuthEndpointTests : IntegrationApiTestBase
{
    public AuthEndpointTests(BattleGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Register_WithValidPayload_ReturnsOk()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        try
        {
            var (status, message) = await ApiIntegrationTestHelper.RegisterAndReadAsync(Client, request);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(ApiIntegrationTestHelper.ExpectedRegisterSuccessMessage, message);
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsBadRequest()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        try
        {
            var first = await ApiIntegrationTestHelper.RegisterAndReadAsync(Client, request);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var duplicate = await ApiIntegrationTestHelper.RegisterAndReadAsync(Client, request);
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }

    [Fact]
    public async Task Register_WithDuplicateUsername_ReturnsBadRequest()
    {
        if (!DatabaseAvailable)
            return;

        var first = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        var second = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        second.UserName = first.UserName;

        try
        {
            var firstResponse = await ApiIntegrationTestHelper.RegisterAndReadAsync(Client, first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var duplicate = await ApiIntegrationTestHelper.RegisterAndReadAsync(Client, second);
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, first.Email);
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, second.Email);
        }
    }

    [Fact]
    public async Task Register_WithMismatchedPasswords_ReturnsBadRequest()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        request.ConfirmPassword = "Different_Password_123!";

        var (status, _) = await ApiIntegrationTestHelper.RegisterAndReadAsync(Client, request);
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokens()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        try
        {
            await ApiIntegrationTestHelper.RegisterUserForSetupAsync(Client, request);

            var tokens = await ApiIntegrationTestHelper.LoginAsync(
                Client, request.Email, request.Password);

            Assert.NotNull(tokens);
            Assert.True(tokens.Success);
            Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
            Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        try
        {
            await ApiIntegrationTestHelper.RegisterUserForSetupAsync(Client, request);

            using var response = await ApiIntegrationTestHelper.LoginPostAsync(
                Client, request.Email, "wrong-password");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }

    [Fact]
    public async Task Login_WithNonExistingEmail_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await ApiIntegrationTestHelper.LoginPostAsync(
            Client, $"missing_{Guid.NewGuid():N}@battlegrid.test", "random-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithNonExistingUsername_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await ApiIntegrationTestHelper.LoginPostAsync(
            Client, $"missing_user_{Guid.NewGuid():N}", "random-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithRefreshToken_ReturnsOk()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        try
        {
            await ApiIntegrationTestHelper.RegisterUserForSetupAsync(Client, request);
            var tokens = await ApiIntegrationTestHelper.LoginAsync(
                Client, request.Email, request.Password);
            Assert.NotNull(tokens);

            using var response = await Client.PostAsJsonAsync(ApiIntegrationTestHelper.LogoutPath,
                new RefreshTokenRequestDto { RefreshToken = tokens.RefreshToken });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ReturnsNewAccessToken()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        try
        {
            await ApiIntegrationTestHelper.RegisterUserForSetupAsync(Client, request);
            var tokens = await ApiIntegrationTestHelper.LoginAsync(
                Client, request.Email, request.Password);
            Assert.NotNull(tokens);

            using var response = await Client.PostAsJsonAsync(ApiIntegrationTestHelper.RefreshPath,
                new RefreshTokenRequestDto { RefreshToken = tokens.RefreshToken });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var refreshed = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.NotNull(refreshed);
            Assert.True(refreshed.Success);
            Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }

    [Fact]
    public async Task UpdatePassword_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.PatchAsJsonAsync("/api/Auth/password", new PasswordUpdateRequestDto
        {
            OldPassword = "old",
            NewPassword = "new",
            ConfirmNewPassword = "new"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePassword_WithAuth_ReturnsOk()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        const string newPassword = "NewSecure_Pass456!";
        try
        {
            await ApiIntegrationTestHelper.RegisterUserForSetupAsync(Client, request);
            var tokens = await ApiIntegrationTestHelper.LoginAsync(
                Client, request.Email, request.Password);
            Assert.NotNull(tokens);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, tokens.AccessToken);

            using var response = await authClient.PatchAsJsonAsync("/api/Auth/password",
                new PasswordUpdateRequestDto
                {
                    OldPassword = request.Password,
                    NewPassword = newPassword,
                    ConfirmNewPassword = newPassword
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var relogin = await ApiIntegrationTestHelper.LoginAsync(Client, request.Email, newPassword);
            Assert.NotNull(relogin);
            Assert.True(relogin.Success);
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }

    [Fact]
    public async Task UpdatePassword_WithMismatchedConfirmation_ReturnsBadRequest()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        try
        {
            await ApiIntegrationTestHelper.RegisterUserForSetupAsync(Client, request);
            var tokens = await ApiIntegrationTestHelper.LoginAsync(
                Client, request.Email, request.Password);
            Assert.NotNull(tokens);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, tokens.AccessToken);

            using var response = await authClient.PatchAsJsonAsync("/api/Auth/password",
                new PasswordUpdateRequestDto
                {
                    OldPassword = request.Password,
                    NewPassword = "Mismatch_New_123!",
                    ConfirmNewPassword = "Mismatch_New_456!"
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }

    [Fact]
    public async Task UpdatePassword_WithSameOldAndNew_ReturnsBadRequest()
    {
        if (!DatabaseAvailable)
            return;

        var request = ApiIntegrationTestHelper.CreateUniqueRegisterRequest();
        try
        {
            await ApiIntegrationTestHelper.RegisterUserForSetupAsync(Client, request);
            var tokens = await ApiIntegrationTestHelper.LoginAsync(
                Client, request.Email, request.Password);
            Assert.NotNull(tokens);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, tokens.AccessToken);

            using var response = await authClient.PatchAsJsonAsync("/api/Auth/password",
                new PasswordUpdateRequestDto
                {
                    OldPassword = request.Password,
                    NewPassword = request.Password,
                    ConfirmNewPassword = request.Password
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, request.Email);
        }
    }
}