using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.ClientLogs.Commands.RecordClientLogs;

/// <summary>A batch from one browser, with how many entries it dropped over its own limit since the last one.</summary>
public sealed record RecordClientLogsCommand(IReadOnlyList<ClientLogEntry> Entries, int Dropped, string? UserAgent) : ICommand<Unit>;
