using System.Threading;
using System.Threading.Tasks;

namespace Framework.Data.Entity
{
    /// <summary>
    /// Storage boundary for a named save owned by the authenticated user.
    /// </summary>
    public interface ISaveRepository
    {
        Task InitializeAsync(CancellationToken cancellationToken);
        Task<SaveRecord> LoadAsync(string saveKey, CancellationToken cancellationToken);
        Task SaveAsync(string saveKey, SaveRecord record, CancellationToken cancellationToken);
    }
}
