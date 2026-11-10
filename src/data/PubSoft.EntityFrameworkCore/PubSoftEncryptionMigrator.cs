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
/// <remarks>Provider note: uses LIMIT batching (MySQL/PostgreSQL/SQLite).</remarks>
public sealed class PubSoftEncryptionMigrator(CredentialEncryptionService encryption)
{
    /// <summary>Rewrites non-GCM values in every annotated column; returns rows rewritten.</summary>
    public async Task<int> MigrateAsync(DbContext context, int batchSize = 500, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var rewritten = 0;
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
                rewritten += await MigrateColumnAsync(context, table, schema, pk[0], column,
                    Encoding.UTF8.GetBytes(aadText), batchSize, ct).ConfigureAwait(false);
            }
        }
        return rewritten;
    }

    private async Task<int> MigrateColumnAsync(DbContext context, string table, string? schema,
        IProperty pk, string column, byte[] aad, int batchSize, CancellationToken ct)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var pkColumn = pk.GetColumnName(StoreObjectIdentifier.Table(table, schema))
            ?? throw new InvalidOperationException($"PK column unresolved on {table}");
        var target = schema is null ? sql.DelimitIdentifier(table)
            : sql.DelimitIdentifier(schema) + "." + sql.DelimitIdentifier(table);
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct).ConfigureAwait(false);
        var rewritten = 0;
        try
        {
            while (true)
            {
                var rows = new List<(object Key, string Value)>();
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        $"SELECT {sql.DelimitIdentifier(pkColumn)}, {sql.DelimitIdentifier(column)} FROM {target} " +
                        $"WHERE {sql.DelimitIdentifier(column)} IS NOT NULL AND {sql.DelimitIdentifier(column)} NOT LIKE 'pubsoft:%' " +
                        $"ORDER BY {sql.DelimitIdentifier(pkColumn)} LIMIT {batchSize}";
                    await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        rows.Add((reader.GetValue(0), reader.GetString(1)));
                }
                if (rows.Count == 0) return rewritten;

                foreach (var (key, value) in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    var replacement = RewriteValue(value, aad);
                    if (replacement is null) continue;
                    await using var update = connection.CreateCommand();
                    update.CommandText =
                        $"UPDATE {target} SET {sql.DelimitIdentifier(column)} = @v WHERE {sql.DelimitIdentifier(pkColumn)} = @k";
                    var vp = update.CreateParameter(); vp.ParameterName = "v"; vp.Value = replacement;
                    var kp = update.CreateParameter(); kp.ParameterName = "k"; kp.Value = key;
                    update.Parameters.Add(vp);
                    update.Parameters.Add(kp);
                    await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    rewritten++;
                }
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private string? RewriteValue(string value, byte[] aad)
    {
        if (value.StartsWith("pubsoft:", StringComparison.Ordinal))
            return null;
        try
        {
            var decrypted = encryption.Decrypt(value, aad);
            return decrypted.ReplacementCiphertext;
        }
        catch (PubSoftDecryptionException)
        {
            return null;
        }
        catch (FormatException)
        {
            // Not decryptable at all - treat as plaintext and encrypt under the active key.
            return encryption.Encrypt(value, aad);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return encryption.Encrypt(value, aad);
        }
    }
}
