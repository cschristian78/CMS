using ClientManagementSystem.Data;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public sealed class AuthService : IAuthService
{
    public const string DefaultPasswordSentinel = "none";

    private readonly IAuthRepository _auth;

    public AuthService(IAuthRepository auth)
    {
        _auth = auth;
    }

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => _auth.EnsureLoginPwSchemaAsync(cancellationToken);

    public async Task<string> GetUserTypeAsync(int userId, CancellationToken cancellationToken = default)
    {
        var userType = await _auth.GetUserTypeAsync(userId, cancellationToken);
        return string.IsNullOrWhiteSpace(userType) ? "Basic" : userType.Trim();
    }

    public Task<IReadOnlyList<AuthCatUserDto>> ListCatUsersAsync(CancellationToken cancellationToken = default)
        => _auth.ListCatLoginUsersAsync(cancellationToken);

    public async Task<AuthSessionDto> LoginAsync(AuthLoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = (request.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Select a CAT user email to sign in.");
        }

        var user = await _auth.FindActiveCatUserByEmailAsync(email, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new InvalidOperationException("That email is not an active CAT department user.");
        }

        var stored = (user.LoginPw ?? DefaultPasswordSentinel).Trim();
        var mustSet = string.Equals(stored, DefaultPasswordSentinel, StringComparison.OrdinalIgnoreCase);

        if (mustSet)
        {
            var newPassword = (request.NewPassword ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                throw new InvalidOperationException("Create a password before launching CMS.");
            }

            if (string.Equals(newPassword, DefaultPasswordSentinel, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Choose a password other than the default value.");
            }

            if (newPassword.Length < 4)
            {
                throw new InvalidOperationException("Password must be at least 4 characters.");
            }

            await _auth.SetLoginPasswordAsync(user.UserID, newPassword, cancellationToken);
            mustSet = false;
        }
        else
        {
            var password = request.Password ?? string.Empty;
            if (!string.Equals(password, stored, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Incorrect password.");
            }
        }

        await _auth.RecordSuccessfulLoginAsync(user.UserID, cancellationToken);

        return new AuthSessionDto
        {
            UserID = user.UserID,
            Email = user.Email,
            FullName = user.UserFullName,
            UserType = string.IsNullOrWhiteSpace(user.UserType) ? "Basic" : user.UserType.Trim(),
            MustSetPassword = mustSet,
        };
    }
}
