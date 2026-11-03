namespace Gma.Modules.Notifications.Persistence;

using System.Data;
using Gma.Framework.Cqrs.UnitOfWork;
using Gma.Modules.Notifications.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

internal sealed class NotificationsUnitOfWork(NotificationsDbContext dbContext)
    : ITransactionalUnitOfWork
{
    private IDbContextTransaction? ownedTransaction;

    public string ModuleName => NotificationsModuleMetadata.Name;

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (this.ownedTransaction is not null)
        {
            throw new InvalidOperationException(
                "The notifications unit of work already owns an active transaction.");
        }

        if (dbContext.Database.CurrentTransaction is not null ||
            !dbContext.Database.IsRelational())
        {
            return;
        }

        this.ownedTransaction = await dbContext.Database
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CommitTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (this.ownedTransaction is null)
        {
            return;
        }

        IDbContextTransaction transaction = this.ownedTransaction;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        this.ownedTransaction = null;
        await transaction.DisposeAsync().ConfigureAwait(false);
    }

    public async Task RollbackTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (this.ownedTransaction is null)
        {
            return;
        }

        IDbContextTransaction transaction = this.ownedTransaction;
        try
        {
            await transaction.RollbackAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            this.ownedTransaction = null;
            try
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
