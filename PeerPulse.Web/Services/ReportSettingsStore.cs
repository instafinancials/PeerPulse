using System;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using PeerPulse.Web.Data;
using PeerPulse.Web.Models.ReportSettings;

namespace PeerPulse.Web.Services;

public sealed class ReportSettingsStore : IReportSettingsStore
{
    private const long MaxRevision = 9_007_199_254_740_990;
    private const int MaxJsonCharacters = 8 * 1024 * 1024;
    private const int MaxJsonBytes = 8 * 1024 * 1024;
    private readonly PeerPulseDbContext _context;
    private readonly ILogger<ReportSettingsStore> _logger;

    public ReportSettingsStore(PeerPulseDbContext context,ILogger<ReportSettingsStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    public Task<JsonElement> GetPreference(int userId, CancellationToken ct) =>
        Execute("dbo.ReportPreference_Get", userId, null, ct);

    public Task<JsonElement> GetSelection(int userId, CancellationToken ct) =>
        Execute("dbo.ReportSelection_Get", userId, null, ct);

    public Task<JsonElement> GetDraft(int userId, Guid draftId, CancellationToken ct)
    {
        if (draftId == Guid.Empty) throw new ArgumentException("The draft ID is invalid.");
        return Execute("dbo.ReportDraft_Get", userId, c =>
            c.Parameters.Add("@DraftId", SqlDbType.UniqueIdentifier).Value = draftId, ct);
    }

    public Task<JsonElement> SavePreference(int userId, SaveReportPreference value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Snapshot caller values before awaiting database operations.
        var currency = value.Currency;
        var format = value.FinancialFormat;
        var rate = value.ExchangeRate;
        var revision = value.ExpectedRevision;
        if (currency is not ("INR" or "USD") ||
            format is not ("absolute" or "thousands" or "lakhs" or "crores") ||
            rate < 1m || rate > 100000m || decimal.Round(rate, 6) != rate ||
            revision < 0 || revision > MaxRevision)
            throw new ArgumentException("Check the currency, format, rate and revision. The rate supports six decimal places.");

        return Execute("dbo.ReportPreference_Save", userId, c =>
        {
            c.Parameters.Add("@Currency", SqlDbType.VarChar, 3).Value = currency;
            c.Parameters.Add("@FinancialFormat", SqlDbType.VarChar, 12).Value = format;
            var parameter = c.Parameters.Add("@ExchangeRate", SqlDbType.Decimal);
            parameter.Precision = 18;
            parameter.Scale = 6;
            parameter.Value = rate;
            c.Parameters.Add("@ExpectedRevision", SqlDbType.BigInt).Value = revision;
        }, ct);
    }

    public async Task<JsonElement> SaveSelection(int userId, SaveReportSelection value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        var peerIds = value.PeerIds?.ToArray();
        var preferenceRevision = value.PreferenceRevision;
        var revision = value.ExpectedRevision;
        if (peerIds == null || peerIds.Length is < 1 or > 500 ||
            peerIds.Any(x => x <= 0) || peerIds.Distinct().Count() != peerIds.Length ||
            preferenceRevision < 1 || preferenceRevision > MaxRevision ||
            revision < 0 || revision > MaxRevision)
            throw new ArgumentException("Select 1–500 unique peers with valid preference and selection revisions.");

        using var ids = new DataTable();
        ids.Columns.Add("PeerId", typeof(int));
        foreach (var id in peerIds.OrderBy(x => x)) ids.Rows.Add(id);
        return await Execute("dbo.ReportSelection_Save", userId, c =>
        {
            c.Parameters.Add(new SqlParameter("@PeerIds", SqlDbType.Structured)
            { TypeName = "dbo.ReportPeerIds", Value = ids });
            c.Parameters.Add("@PreferenceRevision", SqlDbType.BigInt).Value = preferenceRevision;
            c.Parameters.Add("@ExpectedRevision", SqlDbType.BigInt).Value = revision;
        }, ct);
    }

    private async Task<JsonElement> Execute(string procedure, int userId,
        Action<SqlCommand>? configure, CancellationToken cancellationToken)
    {
        if (userId <= 0) throw new UnauthorizedAccessException("A valid signed-in user is required.");
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var watch = Stopwatch.StartNew();
        var connection = _context.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("ReportSettings requires Microsoft SQL Server.");
        var openedHere = connection.State != ConnectionState.Open;
        var operationCompleted = false;
        try
        {
            // This scoped context and its connection belong to EF/DI.
            // Await each operation; do not run parallel commands on this context.
            if (openedHere) await _context.Database.OpenConnectionAsync(ct);
            await using var command = new SqlCommand(procedure, connection)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 20 };
            var transaction = _context.Database.CurrentTransaction;
            if (transaction != null)
                command.Transaction = (SqlTransaction)transaction.GetDbTransaction();

            command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
            configure?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            if (!await reader.ReadAsync(ct) || reader.FieldCount != 1 || await reader.IsDBNullAsync(0, ct))
                throw new InvalidOperationException("The report procedure returned an invalid result.");

            var json = new StringBuilder();
            using (var textReader = reader.GetTextReader(0))
            {
                var buffer = new char[8192];
                int count;
                while ((count = await textReader.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    if (json.Length > MaxJsonCharacters - count)
                        throw new ReportSettingsPayloadTooLargeException();
                    json.Append(buffer, 0, count);
                }
            }
            if (await reader.ReadAsync(ct) || await reader.NextResultAsync(ct))
                throw new InvalidOperationException("The report procedure returned unexpected extra results.");
            var text = json.ToString();
            if (Encoding.UTF8.GetByteCount(text) > MaxJsonBytes)
                throw new ReportSettingsPayloadTooLargeException();
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("The report procedure must return a JSON object.");
            var result = document.RootElement.Clone();
            _logger.LogInformation("Report settings {Procedure} completed in {ElapsedMs} ms", procedure, watch.ElapsedMilliseconds);
            operationCompleted = true;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Report settings {Procedure} cancelled by caller", procedure);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Report settings {Procedure} exceeded its operation timeout", procedure);
            throw;
        }
        catch (SqlException ex) when (ex.Number is >= 52001 and <= 52007)
        {
            _logger.LogWarning("Report settings {Procedure} rejected. SqlNumber={SqlNumber}", procedure, ex.Number);
            throw;
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            _logger.LogWarning("Report settings {Procedure} SQL timeout after {ElapsedMs} ms", procedure, watch.ElapsedMilliseconds);
            throw;
        }
        catch (ReportSettingsPayloadTooLargeException)
        {
            _logger.LogWarning("Report settings {Procedure} exceeded the JSON size limit", procedure);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report settings {Procedure} failed after {ElapsedMs} ms", procedure, watch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            // Keep caller-opened connections untouched. Never dispose _context or connection.
            if (openedHere && connection.State != ConnectionState.Closed)
            {
                try { await _context.Database.CloseConnectionAsync(); }
                catch (Exception closeError) when (!operationCompleted)
                {
                    // Preserve the original SQL/cancellation error.
                    _logger.LogWarning(closeError, "Failed to close report settings connection after an operation failure");
                }
            }
        }
    }
}

public sealed class ReportSettingsPayloadTooLargeException : Exception
{
    public ReportSettingsPayloadTooLargeException() : base("The report settings response is too large.") { }
}
