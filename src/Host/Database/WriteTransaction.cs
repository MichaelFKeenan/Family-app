using FamilyApp.Host.Events;
using FamilyApp.Host.Live;
using FamilyApp.SharedKernel.Persistence;

namespace FamilyApp.Host.Database;

/// <summary>
/// Every write request (POST, PUT, PATCH, DELETE under /api) is one unit of work (ADR-030):
///
/// 1. Begin a transaction on the request's shared connection.
/// 2. Run the endpoint. Modules make their changes and raise events.
/// 3. If the endpoint succeeded (status below 400), dispatch the events: activity log, then
///    handlers in any module. Then commit.
/// 4. Otherwise, or if anything throws, roll back. Nothing half-done is ever saved.
///
/// Reads (GET) don't open a transaction.
/// </summary>
public static class WriteTransaction
{
    public static TBuilder UseWriteTransactions<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
            {
                return await next(context);
            }

            var session = http.RequestServices.GetRequiredService<DbSession>();
            await session.BeginAsync(http.RequestAborted);
            try
            {
                var result = await next(context);

                if (Succeeded(result))
                {
                    var dispatcher = http.RequestServices.GetRequiredService<DomainEventDispatcher>();
                    await dispatcher.DispatchAsync(http.RequestAborted);
                    await session.CommitAsync(http.RequestAborted);

                    // Only now is the change real, so only now tell the other screens (ADR-042).
                    await http.RequestServices.GetRequiredService<LiveUpdates>().PublishAsync(dispatcher.Dispatched);
                }
                else
                {
                    await session.RollbackAsync();
                }

                return result;
            }
            catch
            {
                await session.RollbackAsync();
                throw;
            }
        });
        return builder;
    }

    private static bool Succeeded(object? result) =>
        result is not IStatusCodeHttpResult { StatusCode: >= 400 };
}
