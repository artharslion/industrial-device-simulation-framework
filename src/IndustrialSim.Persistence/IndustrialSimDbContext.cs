using IndustrialSim.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence;

public sealed class IndustrialSimDbContext(DbContextOptions<IndustrialSimDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken = default) =>
        await SaveChangesAsync(cancellationToken);
}
