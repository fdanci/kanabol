using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Attachments.Commands.AddAttachment;

/// <summary>One file, as the form sent it: its name, its size in bytes and its content, which the caller disposes of.</summary>
public sealed record AddAttachmentCommand(Guid TaskId, string FileName, long Size, Stream Content) : ICommand<AttachmentDto>;
