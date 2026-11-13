using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerPulse.Web.Data;
using PeerPulse.Web.Models.Reports;

namespace PeerPulse.Web.Services;

public sealed class PeerComparisonService : IPeerComparisonService
{
    private const int MaximumSelectedPeers = 500;
    private const int MaximumRows = 10_000;
    private const long MaximumTextBytes = 4 * 1024 * 1024;
    private const int MaximumResponseBytes = 8 * 1024 * 1024;

    // A process-wide admission limit, NOT a distributed limit across servers.
    private static readonly SemaphoreSlim ReportSlots = new(4, 4);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PeerPulseDbContext _db;
    private readonly ILogger<PeerComparisonService> _logger;
    private readonly PeerComparisonOptions _options;

    public PeerComparisonService(PeerPulseDbContext db, IOptions<PeerComparisonOptions> options, ILogger<PeerComparisonService> logger)
    {
        _db = db;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ComparisonSelectionDto> GetSelectionAsync(int instaUserId, Guid draftId, CancellationToken cancellationToken)
    {
        Validate(instaUserId, draftId);
        var timer = Stopwatch.StartNew();
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["InstaUserId"] = instaUserId,
            ["DraftId"] = draftId
        });
        var result = await WithConnectionAsync(_options.SelectionProcedure, async connection =>
        {
            using var command = NewCommand(connection, _options.SelectionProcedure);
            AddIdentity(command, instaUserId, draftId);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            RequireSchema(reader, _options.SelectionProcedure, 1,
                ("DraftId", typeof(Guid)), ("CurrencyCode", typeof(string)),
                ("FinancialFormat", typeof(string)), ("UsdInrRate", typeof(decimal)));
            if (!await reader.ReadAsync(cancellationToken)) throw Unavailable();
            RequireValues(reader, _options.SelectionProcedure, 1, 0, 1, 2, 3);
            if (reader.GetGuid(0) != draftId)
                throw new InvalidDataException("Selection returned a different draft.");
            var preferences = new StaticReportPreferences(
                reader.GetString(1), reader.GetString(2), reader.GetDecimal(3));
            if (await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("Selection metadata must contain exactly one row.");
            if (!await reader.NextResultAsync(cancellationToken))
                throw new InvalidDataException("Missing comparison selection result set.");
            RequireSchema(reader, _options.SelectionProcedure, 2,
                ("PeerId", typeof(int)), ("EntityType", typeof(string)),
                ("Identifier", typeof(string)), ("Name", typeof(string)),
                ("CanLoad", typeof(bool)), ("Reason", typeof(string)));

            var peers = new List<ComparisonPeerDto>();
            while (await reader.ReadAsync(cancellationToken))
            {
                if (peers.Count >= MaximumSelectedPeers)
                    throw new ComparisonReportException(ComparisonFailure.SelectionTooLarge,
                        "A comparison report supports up to 500 peers.");

                RequireValues(reader, _options.SelectionProcedure, 2, 0, 4);
                var type = reader.IsDBNull(1) ? "unknown" : reader.GetString(1).Trim();
                var identifier = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
                var canLoad = reader.GetBoolean(4);
                var reason = reader.IsDBNull(5) ? null : reader.GetString(5);

                peers.Add(new ComparisonPeerDto(reader.GetInt32(0), type, identifier,
                    reader.IsDBNull(3) ? null : reader.GetString(3), canLoad, reason));
            }
            if (await reader.NextResultAsync(cancellationToken))
                throw new InvalidDataException("Selection returned an unexpected third result set.");
            if (peers.Count == 0) throw Unavailable();
            return new ComparisonSelectionDto(draftId, peers) { Preferences = preferences };
        }, cancellationToken);

        _logger.LogInformation("Comparison selection loaded. PeerCount: {PeerCount}; DurationMs: {DurationMs}", result.Peers.Count, timer.ElapsedMilliseconds);
        return result;
    }

    public async Task<PeerComparisonReportDto> GetReportAsync(int instaUserId, Guid draftId, int? peerId, string? searchInput, CancellationToken cancellationToken)
    {
        Validate(instaUserId, draftId);
        searchInput = string.IsNullOrWhiteSpace(searchInput)
            ? null : searchInput.Trim().ToUpperInvariant();
        if (peerId is <= 0 || (peerId is null && searchInput is null) || searchInput?.Length > 21)
            throw new ComparisonReportException(ComparisonFailure.InvalidRequest,
                "Select a peer or enter a CIN/LLPIN from this saved selection.");

        if (!await ReportSlots.WaitAsync(0, cancellationToken))
            throw new ComparisonReportException(ComparisonFailure.Busy,
                "Report capacity is busy. Please try again shortly.");

        var timer = Stopwatch.StartNew();
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["InstaUserId"] = instaUserId,
            ["DraftId"] = draftId,
            ["PeerId"] = peerId
        });
        try
        {
            return await WithConnectionAsync(_options.DispatchProcedure, async connection =>
            {
                // Only stored-procedure calls. SQL owns authorization and CIN/LLPIN routing.
                var sqlTimer = Stopwatch.StartNew();
                using var command = NewCommand(connection, _options.DispatchProcedure);
                command.CommandTimeout = 25;
                AddIdentity(command, instaUserId, draftId);
                command.Parameters.Add("@PeerId", SqlDbType.Int).Value = (object?)peerId ?? DBNull.Value;
                command.Parameters.Add("@SearchInput", SqlDbType.VarChar, 64).Value =
                    (object?)searchInput ?? DBNull.Value;

                int resolvedPeerId;
                int entityId;
                string entityType;
                string identifier;
                string payloadKind;
                var sets = new List<List<Dictionary<string, object?>>>();
                var rowCount = 0;
                long textBytes = 0;
                using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    // Always check before accessing fixed column positions.
                    RequireSchema(reader, _options.DispatchProcedure, 1,
                        ("PeerId", typeof(int)), ("EntityId", typeof(int)),
                        ("EntityType", typeof(string)), ("Identifier", typeof(string)),
                        ("PayloadKind", typeof(string)));
                    if (!await reader.ReadAsync(cancellationToken)) throw Unavailable();
                    // SequentialAccess: validate/read each field before moving forward.
                    RequireValues(reader, _options.DispatchProcedure, 1, 0);
                    resolvedPeerId = reader.GetInt32(0);
                    RequireValues(reader, _options.DispatchProcedure, 1, 1);
                    entityId = reader.GetInt32(1);
                    RequireValues(reader, _options.DispatchProcedure, 1, 2);
                    entityType = reader.GetString(2);
                    RequireValues(reader, _options.DispatchProcedure, 1, 3);
                    identifier = reader.GetString(3);
                    RequireValues(reader, _options.DispatchProcedure, 1, 4);
                    payloadKind = reader.GetString(4);
                    if ((entityType != "company" || payloadKind != "CompanyRowsV1") &&
                        (entityType != "llp" || payloadKind != "LlpJsonV1"))
                        throw new InvalidDataException("Unsupported report payload contract.");
                    if (peerId.HasValue && resolvedPeerId != peerId.Value)
                        throw new InvalidDataException("Dispatcher returned a different peer.");
                    _logger.LogInformation(
                        "Dispatcher resolved. EntityType={EntityType}; PayloadKind={PayloadKind}; ResolvedPeerId={ResolvedPeerId}",
                        entityType, payloadKind, resolvedPeerId);
                    if (await reader.ReadAsync(cancellationToken))
                        throw new InvalidDataException("Unexpected dispatcher metadata rows.");
                    if (!await reader.NextResultAsync(cancellationToken))
                        throw new InvalidDataException("Missing child report results.");
                    do
                    {
                        if (sets.Count >= 9 || reader.FieldCount > 100)
                            throw new InvalidDataException("Unexpected report result structure.");
                        var resultNumber = sets.Count + 2; // Dispatcher metadata is result 1.
                        var section = payloadKind == "CompanyRowsV1"
                            ? CompanySections[sets.Count] : "InstaSummary";
                        LogSchema(reader, _options.DispatchProcedure, resultNumber, section);
                        if (payloadKind == "LlpJsonV1")
                            RequireSchema(reader, _options.DispatchProcedure, resultNumber,
                                ("InstaSummary", typeof(string)));
                        var columnNames = Enumerable.Range(0, reader.FieldCount)
                            .Select(reader.GetName).ToArray();
                        var fieldTypes = Enumerable.Range(0, reader.FieldCount)
                            .Select(reader.GetFieldType).ToArray();
                        var rows = new List<Dictionary<string, object?>>();
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            if (++rowCount > MaximumRows) throw TooLarge();
                            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                            for (var index = 0; index < reader.FieldCount; index++)
                            {
                                object? value;
                                if (reader.IsDBNull(index)) value = null;
                                else if (fieldTypes[index] == typeof(string))
                                {
                                    // Stream text to bound memory before materializing a large JSON column.
                                    using var source = reader.GetTextReader(index);
                                    var buffer = new char[4096];
                                    var text = new StringBuilder();
                                    int read;
                                    while ((read = await source.ReadAsync(
                                        buffer.AsMemory(), cancellationToken)) > 0)
                                    {
                                        // UTF-8 may use up to three bytes per UTF-16 code unit.
                                        // Conservative bound also handles chunks splitting surrogate pairs.
                                        textBytes += 3L * read;
                                        if (textBytes > MaximumTextBytes) throw TooLarge();
                                        text.Append(buffer, 0, read);
                                    }
                                    value = text.ToString();
                                }
                                else value = reader.GetValue(index);
                                if (!row.TryAdd(columnNames[index], value))
                                    throw new InvalidDataException("Duplicate report column name.");
                            }
                            rows.Add(row);
                        }
                        sets.Add(rows);
                        _logger.LogInformation(
                            "SQL result read. Procedure={Procedure}; ResultSet={ResultSet}; Section={Section}; Rows={Rows}",
                            _options.DispatchProcedure, resultNumber, section, rows.Count);
                    } while (await reader.NextResultAsync(cancellationToken));
                }

                _logger.LogInformation(
                    "Comparison procedure read completed. ResultSets: {ResultSets}; Rows: {Rows}; SqlDurationMs: {SqlDurationMs}",
                    sets.Count, rowCount, sqlTimer.ElapsedMilliseconds);

                cancellationToken.ThrowIfCancellationRequested();
                JsonElement summary;
                _logger.LogInformation("Report mapping started. PayloadKind={PayloadKind}", payloadKind);
                if (payloadKind == "CompanyRowsV1" && entityType == "company")
                {
                    if (sets.Count != 9 || sets[0].Count != 1)
                        throw new InvalidDataException(
                            $"CompanyRowsV1 expects nine child results and one company row. Received {sets.Count} results.");
                    var mapped = CompanyComparisonMapper.Map(draftId, resolvedPeerId, identifier, sets);
                    summary = JsonSerializer.SerializeToElement(mapped.InstaSummary, JsonOptions);
                }
                else if (payloadKind == "LlpJsonV1" && entityType == "llp")
                {
                    if (sets.Count != 1 || sets[0].Count != 1 ||
                        sets[0][0].Count != 1 ||
                        !sets[0][0].TryGetValue("InstaSummary", out var value) || value is not string json)
                        throw new InvalidDataException("LLP procedure must return one InstaSummary JSON object.");
                    using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("LLP summary must be a JSON object.");
                    summary = document.RootElement.Clone();
                }
                else throw new InvalidDataException("Unsupported report payload contract.");

                var response = new PeerComparisonReportDto(draftId, resolvedPeerId, entityId,
                    entityType, identifier, summary);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions).Length;
                if (bytes > MaximumResponseBytes) throw TooLarge();
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation(
                    "Comparison report mapped. ResponseBytes: {ResponseBytes}; DurationMs: {DurationMs}",
                    bytes, timer.ElapsedMilliseconds);
                return response;
            }, cancellationToken);
        }
        finally { ReportSlots.Release(); }
    }

    private static void Validate(int userId, Guid draftId)
    {
        if (userId <= 0 || draftId == Guid.Empty)
            throw new ComparisonReportException(ComparisonFailure.InvalidRequest,
                "A valid user and report draft are required.");
    }

    private static ComparisonReportException Unavailable() => new(ComparisonFailure.Unavailable, "The selected report or entity is unavailable or no longer eligible.");

    private static ComparisonReportException TooLarge() => new(ComparisonFailure.PayloadTooLarge, "This report exceeds the interactive response limit.");

    private static SqlCommand NewCommand(SqlConnection connection, string text) => new(text, connection)
    {
        CommandType = CommandType.StoredProcedure,
        CommandTimeout = 15
    };

    private static void AddIdentity(SqlCommand command, int userId, Guid draftId)
    {
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        command.Parameters.Add("@DraftId", SqlDbType.UniqueIdentifier).Value = draftId;
    }

    private static readonly string[] CompanySections =
    {
        "CompanyMaster", "Charges", "Directors", "OtherDirectorships",
        "Signatories", "FinancialsSummary", "WebSocialMedia", "RelatedParties",
        "EmployeeAndEstablishmentSummary"
    };

    private void LogSchema(SqlDataReader reader, string procedure, int resultSet, string section)
    {
        if (reader.FieldCount < 1 || reader.FieldCount > 100)
            throw new InvalidDataException($"{procedure}, result {resultSet}: invalid column count {reader.FieldCount}.");
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        _logger.LogInformation(
            "SQL result schema. Procedure={Procedure}; ResultSet={ResultSet}; Section={Section}; FieldCount={FieldCount}; Columns={Columns}",
            procedure, resultSet, section, reader.FieldCount, string.Join(", ", names));
        if (names.Any(string.IsNullOrWhiteSpace) ||
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidDataException($"{procedure}, result {resultSet}: blank or duplicate column aliases.");
    }

    private void RequireSchema(SqlDataReader reader, string procedure, int resultSet,
        params (string Name, Type Type)[] expected)
    {
        LogSchema(reader, procedure, resultSet, "Contract metadata");
        var valid = reader.FieldCount == expected.Length;
        if (valid)
        {
            for (var i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(reader.GetName(i), expected[i].Name, StringComparison.OrdinalIgnoreCase) ||
                    reader.GetFieldType(i) != expected[i].Type)
                {
                    valid = false;
                    break;
                }
            }
        }
        if (valid) return;
        var wanted = string.Join(", ", expected.Select(c => c.Name + ":" + c.Type.Name));
        var actual = string.Join(", ", Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.GetName(i) + ":" + reader.GetFieldType(i).Name));
        _logger.LogError(
            "SQL contract mismatch. Procedure={Procedure}; ResultSet={ResultSet}; Expected={Expected}; Actual={Actual}",
            procedure, resultSet, wanted, actual);
        throw new InvalidDataException(
            $"SQL result contract mismatch in {procedure}, result {resultSet}. Expected [{wanted}]; actual [{actual}].");
    }

    private static void RequireValues(SqlDataReader reader, string procedure, int resultSet,
        params int[] positions)
    {
        foreach (var position in positions)
            if (reader.IsDBNull(position))
                throw new InvalidDataException(
                    $"{procedure}, result {resultSet}: required column {reader.GetName(position)} is NULL.");
    }

    private async Task<T> WithConnectionAsync<T>(string procedure,
        Func<SqlConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var stage = "OpenConnection";
        var connection = _db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("SQL Server is required.");
        var openedHere = connection.State != ConnectionState.Open;
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["Procedure"] = procedure });
        try
        {
            _logger.LogInformation("Comparison SQL call started. Procedure={Procedure}", procedure);
            if (openedHere) await connection.OpenAsync(cancellationToken);
            stage = "ExecuteReadAndMap";
            var result = await action(connection);
            _logger.LogInformation("Comparison SQL call completed. Procedure={Procedure}; DurationMs={DurationMs}",
                procedure, timer.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Comparison SQL call cancelled. Procedure={Procedure}; Stage={Stage}; DurationMs={DurationMs}",
                procedure, stage, timer.ElapsedMilliseconds);
            throw;
        }
        catch (ComparisonReportException exception)
        {
            _logger.LogWarning("Comparison report rejected during SQL processing. Procedure={Procedure}; Failure={Failure}",
                procedure, exception.Failure);
            throw;
        }
        catch (SqlException exception) when (exception.Number is >= 52101 and <= 52110)
        {
            _logger.LogWarning("Comparison SQL rejected request. Procedure={Procedure}; SqlErrorNumber={SqlErrorNumber}; SqlLine={SqlLine}",
                procedure, exception.Number, exception.LineNumber);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Comparison SQL call failed. Procedure={Procedure}; Stage={Stage}; DurationMs={DurationMs}",
                procedure, stage, timer.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            // The scoped DbContext owns this connection; close only if opened here.
            if (openedHere && connection.State != ConnectionState.Closed)
            {
                try { await connection.CloseAsync(); }
                catch (Exception exception)
                { _logger.LogWarning(exception, "Comparison connection cleanup failed."); }
            }
        }
    }
}
