using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Auditworthy.Host.Diagnostics;

/// <summary>
/// TODO(plenipo#176) — drop this once the platform's exception handler honours the status a
/// <see cref="BadHttpRequestException"/> already carries.
/// <para>
/// Issue #72: <c>POST /api/agui/compliance</c> with no body, or with unparseable JSON, answered
/// <b>500</b>. Every other failure on that same route answers cleanly — <c>{}</c> and
/// <c>{"messages":[]}</c> stream <c>RUN_ERROR "No user message supplied."</c> over a 200, and an
/// unknown module streams <c>RUN_ERROR "Unknown module '…'."</c> — so a client that sent nothing
/// was told the server had faulted while a client that sent something meaningless was answered
/// properly. The 500 is the odd one out, not the norm.
/// </para>
/// <para>
/// The cause is a status being discarded, not a status being missing. Minimal-API parameter binding
/// throws <see cref="BadHttpRequestException"/>, and that type already carries
/// <see cref="BadHttpRequestException.StatusCode"/> = 400 — ASP.NET's own contract for "the client
/// sent something I cannot read". The platform then calls <c>app.UseExceptionHandler()</c> with
/// <c>AddProblemDetails()</c> and no handler of its own
/// (<c>src/Plenipo.AspNetCore/Hosting/PlenipoHostSetup.cs:66</c> and <c>:137</c>, read at
/// <c>v0.1.0-alpha.28</c> from source, not from documentation), and the default
/// <c>IProblemDetailsService</c> path writes whatever <c>Response.StatusCode</c> happens to be —
/// which the framework has already set to 500. The carried 400 is simply dropped.
/// </para>
/// <para>
/// The escalation ladder, in order, before writing anything here:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Is it already there?</b> No. The platform registers no <see cref="IExceptionHandler"/> and
/// passes no <c>ExceptionHandlerOptions</c>, so nothing in it inspects the exception type.
/// </description></item>
/// <item><description>
/// <b>Does a product seam cover it?</b> Yes, and this is that seam — which is why this is a
/// three-line registration rather than a request the product waits on. ASP.NET's
/// <c>ExceptionHandlerMiddlewareImpl</c> resolves <c>IEnumerable&lt;IExceptionHandler&gt;</c> from
/// DI and offers each the exception BEFORE falling back to <c>IProblemDetailsService</c>, so a
/// product can answer for an exception class the platform does not without replacing, wrapping or
/// reordering any platform middleware.
/// </description></item>
/// <item><description>
/// <b>Why not middleware in front of the platform?</b> The obvious alternative — read and validate
/// the body in a middleware registered before <c>UsePlenipoPlatform()</c>, the way
/// <c>UseStarterRegisterForCreatedTenants()</c> is — cannot work here: that middleware sits
/// OUTSIDE the platform's exception handler, so by the time control returns to it the 500 has
/// already been written. It would also mean buffering and re-parsing every request body on the
/// route, which is real cost on the streaming path to catch a case the framework has already
/// classified for us.
/// </description></item>
/// </list>
/// <para>
/// Deliberately keyed on the exception TYPE and not on the route. Narrowing this to
/// <c>/api/agui/*</c> would leave the identical 500 on every other bound endpoint in the product
/// and the platform, and would make the rule "this one path is special" rather than "a request the
/// framework could not read is a client error". It is also strictly a status correction: the
/// response body is a ProblemDetails at the status the exception itself nominated, and no request
/// that previously succeeded changes at all.
/// </para>
/// <para>
/// The detail text is a fixed string rather than <see cref="Exception.Message"/> on purpose. The
/// framework's message names the bound parameter and its CLR type ("Failed to read parameter
/// \"RunAgentInput input\" from the request body as JSON"), which is internal shape a client has no
/// business being handed.
/// </para>
/// </summary>
public sealed class BadRequestEnvelopeExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            // Not ours. Returning false hands the exception to the next handler and then to the
            // platform's ProblemDetails fallback, exactly as if this class were not registered.
            return false;
        }

        if (httpContext.Response.HasStarted)
        {
            // A streaming AG-UI turn that fails mid-flight has already sent headers; the status is
            // spent and the failure belongs in-protocol. Let the platform handle it.
            return false;
        }

        var problem = new ProblemDetails
        {
            Status = badRequest.StatusCode,
            Title = "Bad Request",
            Detail = badRequest.StatusCode == StatusCodes.Status400BadRequest
                ? "The request body is missing or is not valid JSON."
                : "The request could not be read.",
        };

        httpContext.Response.StatusCode = badRequest.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", cancellationToken);

        return true;
    }
}
