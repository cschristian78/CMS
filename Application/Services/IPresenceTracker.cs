using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

/// <summary>In-process tracker of signed-in users who recently heartbeated the SPA.</summary>
public interface IPresenceTracker
{
    void Touch(int userId, string fullName, string email, bool hasPhoto);

    void Leave(int userId);

    IReadOnlyList<PresenceUserDto> GetActive(TimeSpan maxAge);
}
