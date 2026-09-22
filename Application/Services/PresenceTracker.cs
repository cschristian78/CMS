using System.Collections.Concurrent;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public sealed class PresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<int, PresenceEntry> _entries = new();

    public void Touch(int userId, string fullName, string email, bool hasPhoto)
    {
        if (userId <= 0)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(fullName) ? (email ?? string.Empty).Trim() : fullName.Trim();
        var mail = (email ?? string.Empty).Trim();
        _entries.AddOrUpdate(
            userId,
            _ => new PresenceEntry(userId, name, mail, hasPhoto, DateTimeOffset.UtcNow),
            (_, existing) => existing with
            {
                FullName = string.IsNullOrWhiteSpace(name) ? existing.FullName : name,
                Email = string.IsNullOrWhiteSpace(mail) ? existing.Email : mail,
                HasPhoto = hasPhoto,
                LastSeenUtc = DateTimeOffset.UtcNow,
            });
    }

    public void Leave(int userId)
    {
        if (userId > 0)
        {
            _entries.TryRemove(userId, out _);
        }
    }

    public IReadOnlyList<PresenceUserDto> GetActive(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var active = new List<PresenceUserDto>();
        foreach (var pair in _entries)
        {
            if (pair.Value.LastSeenUtc < cutoff)
            {
                _entries.TryRemove(pair.Key, out _);
                continue;
            }

            active.Add(new PresenceUserDto
            {
                UserID = pair.Value.UserId,
                FullName = pair.Value.FullName,
                Email = pair.Value.Email,
                HasPhoto = pair.Value.HasPhoto,
                LastSeenUtc = pair.Value.LastSeenUtc,
            });
        }

        return active
            .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.UserID)
            .ToList();
    }

    private sealed record PresenceEntry(
        int UserId,
        string FullName,
        string Email,
        bool HasPhoto,
        DateTimeOffset LastSeenUtc);
}
