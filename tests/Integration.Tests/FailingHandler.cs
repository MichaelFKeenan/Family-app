using FamilyApp.SharedKernel.Events;

namespace FamilyApp.Integration.Tests;

/// <summary>
/// Throws when a task with a magic title is created, so tests can prove that a failing handler
/// rolls back the change that raised the event (ADR-030).
/// </summary>
public sealed class FailingHandler : DomainEventHandler<TaskCreated>
{
    public const string Trigger = "Make the handler fail";

    public override Task HandleAsync(TaskCreated created, CancellationToken cancellationToken) =>
        created.Title == Trigger
            ? throw new InvalidOperationException("Simulated handler failure.")
            : Task.CompletedTask;
}
