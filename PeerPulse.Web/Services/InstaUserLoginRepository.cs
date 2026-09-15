using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PeerPulse.Web.Data;
using PeerPulse.Web.Models.InstaUsers;

namespace PeerPulse.Web.Services;

public sealed class InstaUserLoginRepository
{
    private readonly InstaUsersDbContext _db;

    public InstaUserLoginRepository(InstaUsersDbContext db)
    {
        _db = db;
    }

    public async Task<InstaUser?> FindAsync(string identifier,CancellationToken cancellationToken)
    {
        identifier = identifier.Trim();

        if (identifier.Contains('@'))
        {
            return await FindByEmailAsync(identifier, cancellationToken);
        }

        if (identifier.Length == 10 && identifier.All(char.IsDigit))
        {
            return await FindByMobileAsync(identifier, cancellationToken);
        }

        return null;
    }

    public async Task<InstaUser?> FindByEmailAsync(string email,CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        bool shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = new SqlCommand("dbo.Read_InstaUserByEmail",connection);

            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 30;

            command.Parameters.Add("@p_email", SqlDbType.VarChar).Value = email;

            command.Parameters.Add("@p_InstaUserID", SqlDbType.Int).Value = 0;

            await using SqlDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return MapUser(reader);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<InstaUser?> FindByMobileAsync(string mobileNumber,CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        bool shouldClose = connection.State != ConnectionState.Open;
        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }
            await using var command = new SqlCommand("dbo.Read_InstaUserByNumber",connection);
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 30;
            command.Parameters.Add("@p_number", SqlDbType.VarChar, 10).Value = mobileNumber;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }
            return MapUser(reader);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static InstaUser MapUser(SqlDataReader reader)
    {
        return new InstaUser
        {
            InstaUserId = reader.GetInt32(reader.GetOrdinal("InstaUserID")),
            UserName = GetString(reader, "UserName"),
            UserEmail = GetString(reader, "UserEMail"),
            Password = GetString(reader, "Password"),
            InstaCustomerId = GetNullableInt(reader, "InstaCustomerID"),
            StatusId = GetNullableInt(reader, "StatusID"),
            IsUserActive = GetNullableBool(reader, "IsUserActive"),
            IsGoogleLogin = GetNullableBool(reader, "IsGoogleLogin")
        };
    }

    private static string? GetString(SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt(SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static bool? GetNullableBool(SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal) ? null: reader.GetBoolean(ordinal);
    }
}