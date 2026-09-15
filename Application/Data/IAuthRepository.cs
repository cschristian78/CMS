using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public interface IAuthRepository
{
    Task EnsureLoginPwSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthCatUserDto>> ListCatLoginUsersAsync(CancellationToken cancellationToken = default);

    Task<AuthLoginUserRow?> FindActiveCatUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<string?> GetUserTypeAsync(int userId, CancellationToken cancellationToken = default);

    Task SetLoginPasswordAsync(int userId, string password, CancellationToken cancellationToken = default);

    Task RecordSuccessfulLoginAsync(int userId, CancellationToken cancellationToken = default);
}
