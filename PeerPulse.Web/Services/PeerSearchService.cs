using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PeerPulse.Web.Data;
using PeerPulse.Web.Models;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace PeerPulse.Web.Services
{
    public sealed class PeerSearchService : IPeerSearchService
    {
        private readonly PeerPulseDbContext _context;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public PeerSearchService(PeerPulseDbContext context)
        {
            _context = context;
        }

        public Task<PeerSearchResponse> GetQuotaAsync(int instaUserId,CancellationToken cancellationToken)
        {
            return ExecuteSearchAsync(instaUserId,consume: false,identifierType: null,identifier: null,searchId: null,cancellationToken: cancellationToken);
        }

        public Task<PeerSearchResponse> SearchAsync(int instaUserId,PeerSearchRequest request,CancellationToken cancellationToken)
        {
            string type = (request.IdentifierType ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            string identifier = (request.Identifier ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            string? pattern = type switch
            {
                "PAN" => @"\A[A-Z]{5}[0-9]{4}[A-Z]\z",

                "CIN" =>
                    @"\A[LU][0-9]{5}[A-Z]{2}[0-9]{4}[A-Z]{3}[0-9]{6}\z",

                "LLPIN" => @"\A[A-Z]{3}-[0-9]{4}\z",

                _ => null
            };

            if (request.SearchId == Guid.Empty ||
                pattern is null ||
                !Regex.IsMatch(
                    identifier,
                    pattern,
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)))
            {
                return Task.FromResult(
                    new PeerSearchResponse(
                        Status: "Invalid",
                        Message: "Enter a valid PAN, CIN, or LLPIN.",
                        Quota: null,
                        Records: Array.Empty<PeerRecord>()));
            }

            return ExecuteSearchAsync(
                instaUserId,
                consume: true,
                identifierType: type,
                identifier: identifier,
                searchId: request.SearchId,
                cancellationToken: cancellationToken);
        }

        private Task<PeerSearchResponse> ExecuteSearchAsync(int instaUserId,bool consume,string? identifierType,string? identifier,Guid? searchId,CancellationToken cancellationToken)
        {
            return ExecuteProcedureAsync("dbo.PeerPulse_Search",
                command =>
                {
                    AddParameter(command, "@InstaUserId",DbType.Int32, instaUserId);

                    AddParameter(command, "@Consume",DbType.Boolean, consume);

                    AddParameter(command, "@IdentifierType",DbType.AnsiString, identifierType, 5);

                    AddParameter(command, "@Identifier",DbType.String, identifier, 21);

                    AddParameter(command, "@SearchId",DbType.Guid, searchId);
                },
                async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct))
                    {
                        throw new InvalidOperationException("The search procedure returned no quota information.");
                    }

                    string status =reader.GetString(reader.GetOrdinal("ResultCode"));

                    SearchQuota? quota = null;

                    if (!reader.IsDBNull(reader.GetOrdinal("PlanName")))
                    {
                        quota = new SearchQuota(
                            PlanName:reader.GetString(reader.GetOrdinal("PlanName")),
                            DailyLimit:reader.GetInt32(reader.GetOrdinal("DailyLimit")),
                            Used:reader.GetInt32(reader.GetOrdinal("Used")),
                            UsageDate:reader.GetDateTime(reader.GetOrdinal("UsageDate")));
                    }

                    // Second result set: business profiles.
                    var records = new List<PeerRecord>();

                    if (await reader.NextResultAsync(ct))
                    {
                        while (await reader.ReadAsync(ct))
                        {
                            records.Add(new PeerRecord(EntityType:reader.GetString(reader.GetOrdinal("EntityType")),
                                EntityId:reader.GetInt32(reader.GetOrdinal("EntityId")),
                                RegistrationNumber:reader.GetString(reader.GetOrdinal("RegistrationNumber")),
                                Pan:ReadString(reader, "Pan"),
                                Name:reader.GetString(reader.GetOrdinal("Name")),
                                IncorporatedOn:ReadNullable<DateTime>(reader, "IncorporatedOn"),
                                State:ReadString(reader, "State"),
                                Roc:ReadString(reader, "Roc"),
                                Status:ReadString(reader, "Status"),
                                Activity:ReadString(reader, "Activity"),
                                Address:ReadString(reader, "Address"),
                                Capital:ReadNullable<decimal>(reader, "Capital"),
                                Website:ReadString(reader, "Website"),
                                Partners:ReadNullable<int>(reader, "Partners"),
                                DesignatedPartners:ReadNullable<int>(reader, "DesignatedPartners")));
                        }
                    }

                    string message = status switch
                    {
                        "NoSubscription" =>"No active subscription. Please subscribe to search.",

                        "UnknownPlan" =>"Search allowance is not configured for your plan.",

                        "LimitReached" =>"Daily search limit reached. It resets at midnight India time.",

                        "Invalid" =>"Enter a valid PAN, CIN, or LLPIN.",

                        "Ok" when consume && records.Count == 0 =>"No matching business found. This request uses one search.",

                        "Ok" when consume => $"Found {records.Count} matching business(es).",
                        _ => string.Empty
                    };

                    return new PeerSearchResponse(Status: status,Message: message,Quota: quota,Records: records,SearchId:status == "Ok" && consume ? searchId : null);
                },
                cancellationToken);
        }

        public Task<PeerWorkspaceResponse> WorkspaceAsync(int instaUserId,string action,PeerMutationRequest? request,CancellationToken cancellationToken)
        {
            var input = request ?? new PeerMutationRequest();

            return ExecuteProcedureAsync("dbo.PeerPulse_Workspace",
                command =>
                {
                    AddParameter(command, "@InstaUserId",DbType.Int32, instaUserId);

                    AddParameter(command, "@Action",DbType.AnsiString, action, 30);

                    AddParameter(command, "@SearchId",DbType.Guid, input.SearchId);

                    AddParameter(command, "@EntityType",DbType.AnsiString, input.EntityType, 10);

                    AddParameter(command, "@EntityId",DbType.Int32, input.EntityId);

                    AddParameter(command, "@PeerId",DbType.Int32, input.PeerId);

                    AddParameter(command, "@GroupId",DbType.Int32, input.GroupId);

                    AddParameter(command, "@Name",DbType.String, input.Name?.Trim(), 100);

                    AddParameter(command, "@Vertical",DbType.String, input.Vertical, 150);

                    AddParameter(command, "@Description",DbType.String, input.Description, 1000);

                    AddParameter(command, "@Notes",DbType.String, input.Notes, 1000);

                    AddParameter(command, "@GroupIds",DbType.String,JsonSerializer.Serialize(input.GroupIds ?? Array.Empty<int>()),-1);
                },
                async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct) ||reader.IsDBNull(reader.GetOrdinal("Payload")))
                    {
                        throw new InvalidOperationException("The workspace procedure returned no JSON payload.");
                    }

                    string payload =reader.GetString(reader.GetOrdinal("Payload"));

                    return JsonSerializer.Deserialize<PeerWorkspaceResponse>(
                        payload,
                        JsonOptions)
                        ?? throw new InvalidOperationException(
                            "The workspace response could not be deserialized.");
                },
                cancellationToken);
        }

        private async Task<T> ExecuteProcedureAsync<T>(string procedure,Action<DbCommand> configure,Func<DbDataReader, CancellationToken, Task<T>> read,CancellationToken cancellationToken)
        {
            // This connection belongs to EF Core: do not dispose it here.
            DbConnection connection = _context.Database.GetDbConnection();

            bool openedHere = connection.State != ConnectionState.Open;

            try
            {
                if (openedHere)
                {
                    await _context.Database.OpenConnectionAsync(cancellationToken);
                }

                await using DbCommand command =connection.CreateCommand();

                command.CommandText = procedure;
                command.CommandType = CommandType.StoredProcedure;
                command.CommandTimeout = 30;

                // Participate if the caller already opened an EF transaction.
                var transaction = _context.Database.CurrentTransaction;

                if (transaction is not null)
                {
                    command.Transaction = transaction.GetDbTransaction();
                }

                configure(command);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                return await read(reader, cancellationToken);
            }
            finally
            {
                // Leave caller-owned open connections untouched.
                if (openedHere &&
                    connection.State != ConnectionState.Closed)
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }
        }

        private static void AddParameter(DbCommand command,string name,DbType type,object? value,int size = 0)
        {
            DbParameter parameter = command.CreateParameter();

            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value ?? DBNull.Value;

            if (size != 0)
            {
                parameter.Size = size;
            }

            command.Parameters.Add(parameter);
        }

        private static string? ReadString(DbDataReader reader,string column)
        {
            int ordinal = reader.GetOrdinal(column);

            return reader.IsDBNull(ordinal)
                ? null
                : reader.GetString(ordinal);
        }

        private static T? ReadNullable<T>(DbDataReader reader,string column) where T : struct
        {
            int ordinal = reader.GetOrdinal(column);

            return reader.IsDBNull(ordinal)? null : reader.GetFieldValue<T>(ordinal);
        }
    }
}
