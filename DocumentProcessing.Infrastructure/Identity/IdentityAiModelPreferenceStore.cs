using DocumentProcessing.Application.Services.Ai;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DocumentProcessing.Infrastructure.Identity;

/// <summary>Stores the model choice on the user's Identity record.</summary>
public class IdentityAiModelPreferenceStore : IAiModelPreferenceStore
{
    private readonly AppDbContext _dbContext;

    public IdentityAiModelPreferenceStore(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<AiModelType?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        _dbContext.Users.Where(u => u.Id == userId).Select(u => u.PreferredAiModelType).FirstOrDefaultAsync(cancellationToken);

    public async Task SetAsync(string userId, AiModelType modelType, CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.PreferredAiModelType, modelType), cancellationToken);
        if (updated == 0)
            throw new InvalidOperationException("The signed-in user no longer exists.");
    }
}
