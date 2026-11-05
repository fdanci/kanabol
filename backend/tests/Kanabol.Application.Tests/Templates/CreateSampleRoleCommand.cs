using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Tests.Templates;

/// <summary>Only here to show the shape of a handler's tests, until the first real handler exists.</summary>
public sealed record CreateSampleRoleCommand(string Name) : ICommand<Guid>;
