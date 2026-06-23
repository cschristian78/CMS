namespace ClientManagementSystem.Services;

/// <summary>Filesystem-backed profile photos under <c>App_Data/UserPhotos</c> as <c>{UserID}.ext</c>.</summary>
public interface IUserPhotoStorage
{
    bool TryGetPhotoFile(int userId, out string physicalPath, out string contentType);

    Task SavePhotoAsync(int userId, Stream stream, long? declaredLength, CancellationToken cancellationToken);

    void DeletePhotos(int userId);

    void CopyPhotos(int sourceUserId, int destUserId);
}
