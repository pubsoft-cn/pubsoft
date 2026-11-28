using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace PubSoft.EntityFrameworkCore;

/// <summary>
/// Executes a retryable operation in one explicit EF database transaction.
/// This is not a distributed-transaction or multi-DbContext abstraction.
/// </summary>
public static class EfCoreTransactionExecutor
{
    private static readonly AsyncLocal<LogicalTransaction?> CurrentLogicalTransaction = new();

    /// <summary>
    /// Executes <paramref name="operation"/> in a single explicit database transaction using the context's EF execution strategy.
    /// Nested calls on the same context join the root logical transaction; nested calls on another context fail closed.
    /// </summary>
    /// <remarks>
    /// The operation must be retry-safe: before retry attempts the executor clears the context ChangeTracker, so the operation
    /// must re-read/re-apply all changes on every invocation. <paramref name="verifySucceeded"/> is invoked only when commit outcome
    /// is ambiguous. If omitted in that situation, the executor throws <see cref="EfCoreTransactionCommitVerificationException"/>
    /// rather than retrying a transaction that may already have committed.
    /// </remarks>
    public static Task ExecuteInTransactionAsync<TDbContext>(
        TDbContext dbContext,
        Func<TDbContext, CancellationToken, Task> operation,
        Func<TDbContext, CancellationToken, Task<bool>>? verifySucceeded = null,
        IsolationLevel? isolationLevel = null,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteInTransactionAsync<TDbContext, bool>(
            dbContext,
            async (context, cancellation) =>
            {
                await operation(context, cancellation).ConfigureAwait(false);
                return true;
            },
            verifySucceeded,
            isolationLevel,
            cancellationToken);
    }

    /// <summary>
    /// Executes <paramref name="operation"/> in a single explicit database transaction using the context's EF execution strategy.
    /// Nested calls on the same context join the root logical transaction; nested calls on another context fail closed.
    /// </summary>
    /// <inheritdoc cref="ExecuteInTransactionAsync{TDbContext}(TDbContext, Func{TDbContext, CancellationToken, Task}, Func{TDbContext, CancellationToken, Task{bool}}?, IsolationLevel?, CancellationToken)"/>
    public static Task<TResult> ExecuteInTransactionAsync<TDbContext, TResult>(
        TDbContext dbContext,
        Func<TDbContext, CancellationToken, Task<TResult>> operation,
        Func<TDbContext, CancellationToken, Task<bool>>? verifySucceeded = null,
        IsolationLevel? isolationLevel = null,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(operation);

        var current = CurrentLogicalTransaction.Value;
        if (current is not null)
            return ExecuteNested(dbContext, operation, current, isolationLevel, cancellationToken);

        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("EfCoreTransactionExecutor cannot compose with an existing DbContext transaction; make the executor call the transaction root.");
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("EfCoreTransactionExecutor does not support ambient TransactionScope; use explicit EF transactions.");

        var rootIsolationLevel = isolationLevel ?? IsolationLevel.ReadCommitted;
        var state = new ExecutorState<TDbContext, TResult>(operation, verifySucceeded, rootIsolationLevel);
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            state,
            ExecuteAttemptAsync<TDbContext, TResult>,
            VerifyCommitAsync<TDbContext, TResult>,
            cancellationToken);
    }

    private static Task<TResult> ExecuteNested<TDbContext, TResult>(
        TDbContext dbContext,
        Func<TDbContext, CancellationToken, Task<TResult>> operation,
        LogicalTransaction current,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken)
        where TDbContext : DbContext
    {
        if (!ReferenceEquals(current.DbContext, dbContext))
            throw new InvalidOperationException("Nested EfCoreTransactionExecutor calls cannot span multiple DbContext instances; no distributed transaction is created.");
        if (isolationLevel.HasValue && isolationLevel.Value != current.IsolationLevel)
            throw new InvalidOperationException("Nested EfCoreTransactionExecutor calls cannot change the root transaction isolation level.");
        return operation(dbContext, cancellationToken);
    }

    private static async Task<TResult> ExecuteAttemptAsync<TDbContext, TResult>(
        DbContext dbContext,
        ExecutorState<TDbContext, TResult> state,
        CancellationToken cancellationToken)
        where TDbContext : DbContext
    {
        var typedContext = (TDbContext)dbContext;
        if (state.AttemptCount > 0)
            dbContext.ChangeTracker.Clear();
        state.AttemptCount++;
        state.CommitMayHaveSucceeded = false;
        state.CommitException = null;

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(state.IsolationLevel, cancellationToken)
            .ConfigureAwait(false);

        var previous = CurrentLogicalTransaction.Value;
        CurrentLogicalTransaction.Value = new LogicalTransaction(dbContext, state.IsolationLevel);
        try
        {
            state.Result = new ResultHolder<TResult>(await state.Operation(typedContext, cancellationToken).ConfigureAwait(false));
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            CurrentLogicalTransaction.Value = previous;
        }

        state.CommitMayHaveSucceeded = true;
        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            state.CommitException = exception;
            dbContext.ChangeTracker.Clear();
            throw;
        }
        return state.Result.Value;
    }

    private static async Task<ExecutionResult<TResult>> VerifyCommitAsync<TDbContext, TResult>(
        DbContext dbContext,
        ExecutorState<TDbContext, TResult> state,
        CancellationToken cancellationToken)
        where TDbContext : DbContext
    {
        if (!state.CommitMayHaveSucceeded)
            return new ExecutionResult<TResult>(false, default!); // pubsoft:exempt PUBSOFT046 "failed execution results ignore the result payload"
        if (state.VerifySucceeded is null)
            throw new EfCoreTransactionCommitVerificationException(state.CommitException);

        var succeeded = await state.VerifySucceeded((TDbContext)dbContext, cancellationToken).ConfigureAwait(false);
        var result = state.Result ?? throw new InvalidOperationException("The transaction result was not captured before commit verification.");
        return new ExecutionResult<TResult>(succeeded, result.Value);
    }

    private sealed class LogicalTransaction(DbContext dbContext, IsolationLevel isolationLevel)
    {
        public DbContext DbContext { get; } = dbContext;
        public IsolationLevel IsolationLevel { get; } = isolationLevel;
    }

    private sealed class ResultHolder<TResult>(TResult value)
    {
        public TResult Value { get; } = value;
    }

    private sealed class ExecutorState<TDbContext, TResult>(
        Func<TDbContext, CancellationToken, Task<TResult>> operation,
        Func<TDbContext, CancellationToken, Task<bool>>? verifySucceeded,
        IsolationLevel isolationLevel)
        where TDbContext : DbContext
    {
        public Func<TDbContext, CancellationToken, Task<TResult>> Operation { get; } = operation;
        public Func<TDbContext, CancellationToken, Task<bool>>? VerifySucceeded { get; } = verifySucceeded;
        public IsolationLevel IsolationLevel { get; } = isolationLevel;
        public int AttemptCount { get; set; }
        public bool CommitMayHaveSucceeded { get; set; }
        public Exception? CommitException { get; set; }
        public ResultHolder<TResult>? Result { get; set; }
    }
}
