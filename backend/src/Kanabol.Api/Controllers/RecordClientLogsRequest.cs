using Kanabol.Application.Features.ClientLogs;

namespace Kanabol.Api.Controllers;

public sealed record RecordClientLogsRequest(IReadOnlyList<ClientLogEntry> Entries, int Dropped = 0);
