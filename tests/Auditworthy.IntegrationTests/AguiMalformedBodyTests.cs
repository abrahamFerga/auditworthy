using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// A malformed request envelope on <c>POST /api/agui/{moduleId}</c> is a CLIENT error (#72).
/// </summary>
/// <remarks>
/// <para>
/// The route already answers every <i>semantic</i> failure in-protocol — <c>{}</c> and
/// <c>{"messages":[]}</c> both stream <c>RUN_ERROR "No user message supplied."</c> over a 200, and
/// an unknown module streams <c>RUN_ERROR "Unknown module '…'."</c>. Only the envelope itself
/// failed differently: an absent body or unparseable JSON surfaced as <b>500</b>, so a client that
/// sent nothing was told the server had faulted while a client that sent something meaningless got
/// a clean answer. That inversion is what #72 is about.
/// </para>
/// <para>
/// The cause is not in this repo: minimal-API parameter binding throws
/// <c>BadHttpRequestException</c>, which already carries <c>StatusCode = 400</c>, and the
/// platform's <c>ExceptionHandlerMiddleware</c> discards that status and writes the generic 500
/// ProblemDetails. The product shim is
/// <c>Auditworthy.Host.Diagnostics.BadRequestEnvelopeExceptionHandler</c>; see its comments for the
/// escalation ladder and the <c>TODO(plenipo#176)</c> that retires it.
/// </para>
/// <para>
/// These assert on the transport, so they go through real HTTP with dev-auth headers via
/// <see cref="IntegrationFixture.AdminClient"/>. <c>AuthorizedScopeAsync()</c> never touches the
/// middleware pipeline and so could not fail while the bug was live.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class AguiMalformedBodyTests(IntegrationFixture fixture)
{
    private const string Route = "/api/agui/compliance";

    [Fact]
    public async Task An_absent_body_is_a_client_error_not_a_server_fault()
    {
        using var client = fixture.AdminClient(subject: "agui-envelope-admin");

        // No content at all — the exact first curl in #72. HttpRequestMessage with a null Content
        // sends no body and no Content-Type, which is what a bare `curl -X POST` does.
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unparseable_body_is_a_client_error_not_a_server_fault()
    {
        using var client = fixture.AdminClient(subject: "agui-envelope-admin");

        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The contrast that makes the two assertions above a fix rather than a blanket 400: a body
    /// that PARSES but says nothing useful must still be answered in-protocol over a 200. Without
    /// this, "return 400 for anything the runner dislikes" would pass, and that would be a worse
    /// regression than the one being fixed.
    /// </summary>
    [Fact]
    public async Task A_well_formed_but_empty_envelope_still_answers_in_protocol()
    {
        using var client = fixture.AdminClient(subject: "agui-envelope-admin");

        using var response = await client.PostAsJsonAsync(Route, new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stream = AguiStream.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(stream.Failed, "expected an in-protocol RUN_ERROR, got: " + string.Join(",", stream.EventTypes));
        Assert.Contains("No user message supplied", stream.Error);
    }
}
