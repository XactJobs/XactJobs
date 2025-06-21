using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace XactJobs.Internal.SqlDialects
{
    internal static class DatabaseFacadeExtensions
    {
        internal static async Task<int?> ExecuteScalarIntAsync(this DatabaseFacade database, string sql, CancellationToken cancellationToken)
        {
            using var command = database.GetDbConnection().CreateCommand();

            command.CommandText = sql;
            command.Transaction = database.CurrentTransaction?.GetDbTransaction();

            await database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

            return result != null ? Convert.ToInt32(result) : null;
        }

        internal static async Task<int?> ExecuteOutputIntAsync(this DatabaseFacade database, string sql, CancellationToken cancellationToken)
        {
            using var command = database.GetDbConnection().CreateCommand();

            command.CommandText = sql;
            command.Transaction = database.CurrentTransaction?.GetDbTransaction();

            var pResult = command.CreateParameter();
            pResult.ParameterName = "@result";
            pResult.Direction = System.Data.ParameterDirection.Output;
            pResult.DbType = System.Data.DbType.Int32;
            command.Parameters.Add(pResult);

            await database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            return pResult.Value != null ? Convert.ToInt32(pResult.Value) : null;
        }

    }
}
