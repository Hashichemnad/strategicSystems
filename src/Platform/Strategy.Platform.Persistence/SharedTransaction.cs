using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Strategy.Platform.Persistence;

/// <summary>
/// Lets a second component's DbContext join the transaction another context opened on the same database (ADR 0023), so a module's record
/// and the approval engine's instance (with their audit rows and outbox messages) commit together.
/// </summary>
public static class SharedTransaction
{
    /// <summary>
    /// Joins the open transaction of <paramref name="transactionContext"/> (a <see cref="DbContext"/>). Returns false, and changes nothing,
    /// when it is null or has no open transaction. Call before this context opens its own connection in the scope.
    /// </summary>
    public static async Task<bool> JoinTransactionAsync(this DbContext context, object? transactionContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (transactionContext is not DbContext owner || owner.Database.CurrentTransaction is not { } transaction)
        {
            return false;
        }

        var dbTransaction = transaction.GetDbTransaction();
        if (ReferenceEquals(context, owner) || context.Database.CurrentTransaction?.GetDbTransaction() == dbTransaction)
        {
            return true;
        }

        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("The context already has its own transaction and cannot join another one.");
        }

        var connection = owner.Database.GetDbConnection();
        if (!ReferenceEquals(context.Database.GetDbConnection(), connection))
        {
            context.Database.SetDbConnection(connection, contextOwnsConnection: false);
        }

        await context.Database.UseTransactionAsync(dbTransaction, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
