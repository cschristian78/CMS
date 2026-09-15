using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public interface IAuthService
{
    Task<IReadOnlyList<AuthCatUserDto>> ListCatUsersAsync(CancellationToken cancellationToken = default);

    Task<AuthSessionDto> LoginAsync(AuthLoginRequest request, CancellationToken cancellationToken = default);

    Task<string> GetUserTypeAsync(int userId, CancellationToken cancellationToken = default);

    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
}
