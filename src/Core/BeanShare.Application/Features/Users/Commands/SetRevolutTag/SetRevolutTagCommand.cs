using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;

namespace BeanShare.Application.Features.Users.Commands.SetRevolutTag;

/// <summary>Set (or clear, with null/empty) the current user's Revolut handle for settlement payment links.</summary>
public sealed record SetRevolutTagCommand(string? RevolutTag) : ICommand<Result>;
