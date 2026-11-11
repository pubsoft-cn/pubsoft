using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using PubSoft.Cryptography;

namespace PubSoft.EntityFrameworkCore;

/// <summary>
/// Rewrites plaintext and legacy-ciphertext values in <see cref="EncryptionModelBuilderExtensions.HasPubSoftEncryption"/>
/// columns to the versioned GCM envelope. AAD is read from the EF model annotation written by
/// HasPubSoftEncryption - the same convention encrypts at write time, so callers cannot supply
/// a mismatched AAD. Rows already under the current scheme (pubsoft: prefix) are skipped;
/// in-ring key-id rotation uses <see cref="CredentialEncryptionService.Rotate"/>.
/// </summary>
/// <remarks>
/// Fail-closed on ambiguity: a value the service cannot decrypt is counted in
/// <see cref="EncryptionMigrationReport.SkippedAmbiguous"/> and left untouched - it may be
/// plaintext or legacy ciphertext under an unavailable key, and writing over an undecryptable
/// value destroys the original. Pass <c>treatUndecryptableAsPlaintext: true</c> only with a
/// verified backup. Updates compare-and-swap on the original value so a concurrent writer is
/// skipped, not clobbered. Run in a maintenance window; provider note: LIMIT batching
/// (MySQL/PostgreSQL/SQLite).
/// </remarks>
public sealed class PubSoftEncryptionMigrator(CredentialEncryptionService encryption)
{
    /// <summary>Rewrites non-GCM values in every annotated column.</summary>
    /// <param name="context">The context whose model carries the encryption AAD annotations.</param>
    /// <param name="batchSize">Rows read per batch; must be positive.</param>
    /// <param name="treatUndecryptableAsPlaintext">DANGEROUS opt-in: values that fail decryption
    /// are re-encrypted as plaintext. Only use after verifying they really are plaintext.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<EncryptionMigrationReport> MigrateAsync(
        DbContext context, int batchSize = 500, bool treatUndecryptableAsPlaintext = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var report = new EncryptionMigrationReport();
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            var schema = entityType.GetSchema();
            if (table is null) continue;
            var pk = entityType.FindPrimaryKey()?.Properties;
            if (pk is not { Count: 1 }) continue;

            foreach (var property in entityType.GetProperties())
            {
                var aadText = property.FindAnnotation(EncryptionModelBuilderExtensions.EncryptionAadAnnotation)
                    ?.Value as string;
                if (aadText is null) continue;
                var column = property.GetColumnName(StoreObjectIdentifier.Table(table, schema));
                if (column is null) continue;
                await MigrateColumnAsync(context, table, schema, pk[0], column,
                    Encoding.UTF8.GetBytes(aadText), batchSize, treatUndecryptableAsPlaintext,
                    report, ct).ConfigureAwait(false);
            }
        }
        return report;
    }

    private async Task MigrateColumnAsync(DbContext context, string table, string? schema,
        IProperty pk, string column, byte[] aad, int batchSize, bool asPlaintext,
        EncryptionMigrationReport report, CancellationToken ct)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var pkColumn = pk.GetColumnName(StoreObjectIdentifier.Table(table, schema))
            ?? throw new InvalidOperationException($"PK column unresolved on {table}");
        var target = schema is null ? sql.DelimitIdentifier(table)
            : sql.DelimitIdentifier(schema) + "." + sql.DelimitIdentifier(table);
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            object? lastKey = null;
            while (true)
            {
                var rows = new List<(object Key, string Value)>();
                await using (var cmd = connection.CreateCommand())
                {
                    // keyset pagination: skipped/conflicted rows are passed-over via the
                    // cursor so they cannot be re-selected into an infinite batch loop.
                    var cursor = lastKey is null ? "" : $"AND {sql.DelimitIdentifier(pkColumn)} > @last ";
                    cmd.CommandText =
                        $"SELECT {sql.DelimitIdentifier(pkColumn)}, {sql.DelimitIdentifier(column)} FROM {target} " +
                        $"WHERE {sql.DelimitIdentifier(column)} IS NOT NULL AND {sql.DelimitIdentifier(column)} NOT LIKE 'pubsoft:%' " +
                        cursor +
                        $"ORDER BY {sql.DelimitIdentifier(pkColumn)} LIMIT {batchSize}";
                    if (lastKey is not null)
                    {
                        var lp = cmd.CreateParameter(); lp.ParameterName = "last"; lp.Value = lastKey;
                        cmd.Parameters.Add(lp);
                    }
                    await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        rows.Add((reader.GetValue(0), reader.GetString(1)));
                }
                if (rows.Count == 0) return;
                lastKey = rows[^1].Key;

                foreach (var (key, value) in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    var (replacement, skipped) = RewriteValue(value, aad, asPlaintext);
                    if (skipped) { report.SkippedAmbiguous++; continue; }
                    if (replacement is null) continue;
                    await using var update = connection.CreateCommand();
                    update.CommandText =
                        $"UPDATE {target} SET {sql.DelimitIdentifier(column)} = @v " +
                        $"WHERE {sql.DelimitIdentifier(pkColumn)} = @k AND {sql.DelimitIdentifier(column)} = @o";
                    var vp = update.CreateParameter(); vp.ParameterName = "v"; vp.Value = replacement;
                    var kp = update.CreateParameter(); kp.ParameterName = "k"; kp.Value = key;
                    var op = update.CreateParameter(); op.ParameterName = "o"; op.Value = value;
                    update.Parameters.Add(vp);
                    update.Parameters.Add(kp);
                    update.Parameters.Add(op);
                    // CAS on the original value - a concurrent writer wins; we skip instead
                    // of clobbering the newer value.
                    if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
                        report.Rewritten++;
                    else
                        report.Conflicts++;
                }
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private (string? Replacement, bool Skipped) RewriteValue(string value, byte[] aad, bool asPlaintext)
    {
        try
        {
            var decrypted = encryption.Decrypt(value, aad);
            return (decrypted.ReplacementCiphertext, false);
        }
        catch (PubSoftDecryptionException)
        {
            return (null, false);
        }
        catch (Exception) when (asPlaintext)
        {
            // Opt-in: caller asserts these values are plaintext.
            return (encryption.Encrypt(value, aad), false);
        }
        catch (Exception)
        {
            // Undecryptable and NOT provably plaintext: could be legacy ciphertext under an
            // unavailable key or corrupted data. Never overwrite - report and leave it.
            return (null, true);
        }
    }
}

/// <summary>Per-run outcome of <see cref="PubSoftEncryptionMigrator.MigrateAsync"/>.</summary>
public sealed class EncryptionMigrationReport
{
    /// <summary>Rows successfully re-encrypted and persisted.</summary>
    public int Rewritten { get; internal set; }

    /// <summary>Values that failed decryption and were left untouched (ambiguous plaintext vs
    /// undecryptable ciphertext). Investigate before opting into plaintext rewriting.</summary>
    public int SkippedAmbiguous { get; internal set; }

    /// <summary>Rows skipped because a concurrent writer changed the value between read and update.</summary>
    public int Conflicts { get; internal set; }
}
