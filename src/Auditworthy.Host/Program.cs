using Auditworthy.Compliance;
using Auditworthy.Host.Authorization;
using Auditworthy.Host.Identity;
using Microsoft.AspNetCore.Authorization;
using Plenipo.Application.Authorization;
using Plenipo.AspNetCore.Hosting;
using Plenipo.AspNetCore.Identity;
using Plenipo.AspNetCore.Modules;

// Auditworthy — a thin product host on the Plenipo platform. Everything that would normally be
// "the backbone" (auth, tenancy, RBAC, approvals, audit, jobs, chat transports, documents, OCR,
// RAG, admin console) arrives with AddPlenipoPlatform(). This file is the seam list; all the real
// code lives in Auditworthy.Compliance.
//
// NOTE: the host API is AddPlenipoPlatform() / UsePlenipoPlatform(). The platform's own
// BUILDING_A_PRODUCT.md documents AddPlenipo() / UsePlenipo() — those do not exist.

var builder = WebApplication.CreateBuilder(args);

builder.AddPlenipoPlatform();
builder.AddPlenipoModule<ComplianceModule>();

// TODO(plenipo#149) — drop both lines once the platform prefers the persisted user record.
// The platform enriches the request context with the TOKEN's name claim rather than the persisted
// User.DisplayName, which puts a constant in the audit's actor column and in the approvals queue's
// proposer. See PersistedDisplayNameEnricher for why this product will not wait on it. Registered
// AFTER AddPlenipoPlatform() on purpose: the platform uses AddScoped, not TryAdd, so last wins.
builder.Services.AddScoped<RequestEnricher>();
builder.Services.AddScoped<IRequestEnricher, PersistedDisplayNameEnricher>();

// TODO(plenipo#155) — drop this once the platform can gate its ADMT disclosure surface.
// GET /api/platform/ai-decisions is mapped by the platform with a bare RequireAuthorization(), so
// any authenticated tenant member reads it — and in THIS product those rows are compliance-register
// content that ComplianceModule.cs:293 already gates behind compliance.view on
// /api/compliance/controls (and :101 on the Controls tab that reads it). Source, not SPEC.md: that
// permission string is absent from the spec. See AiDecisionDisclosureGuard for why this
// is an additive IAuthorizationHandler rather than the "last wins" replacement used above.
builder.Services.AddSingleton<IAuthorizationHandler, AiDecisionDisclosureGuard>();

// ── Role baselines ────────────────────────────────────────────────────────────────────────────
// These are the SHIPPED starting points; they are runtime-editable per tenant in the admin
// console. Roles narrow what RBAC allows — nothing here grants beyond it.

// Auditors and executives: read the register, run the analysis, read the evidence. No writes.
builder.Services.AddPlenipoRole("compliance-reader",
[
    "chat.use", "chat.conversations.view", "files.read",
    "tools.documents.read_document", "tools.documents.list_documents",
    ComplianceModule.ViewCompliance,
    "tools.compliance.list_controls",
    "tools.compliance.get_control",
]);

// THE LOAD-BEARING ROLE. An analyst may PROPOSE state changes and may approve NONE of them, and
// is deliberately excluded from chat.approvals.manage so they cannot clear their own gate.
// An enumerated allowlist, never a wildcard — a wildcard here would silently hand the analyst
// every future write tool the module gains, including the ones they must not hold.
// If this exclusion is ever lost, the approval lane becomes ceremony and the product's central
// claim ("an accountable owner approves before anything is committed") becomes false.
builder.Services.AddPlenipoRole("compliance-analyst",
[
    "chat.use", "chat.conversations.view", "files.read", "files.upload",
    "tools.documents.read_document", "tools.documents.list_documents",
    ComplianceModule.ViewCompliance,
    "tools.compliance.list_controls",
    "tools.compliance.get_control",
    "tools.compliance.propose_control_change",
]);

// The accountable owner: everything the module does, plus the ability to clear a parked approval.
builder.Services.AddPlenipoRole("compliance-owner",
[
    "chat.use", "chat.conversations.view", "files.read", "files.upload",
    "tools.documents.read_document", "tools.documents.list_documents",
    ComplianceModule.ViewCompliance,
    ComplianceModule.ManageCompliance,
    "tools.compliance.*",
    Permissions.ManageApprovals,
]);

var app = builder.Build();

// RunPlenipoPlatformAsync = UsePlenipoPlatform() + InitializePlenipoAsync() + RunAsync().
//
// Do NOT reduce this to `app.UsePlenipoPlatform(); app.Run();`. UsePlenipoPlatform only configures
// the pipeline; InitializePlenipoAsync is what applies the platform and audit migrations, then each
// module's migrations and seed data. Without it the app starts and serves 500s forever, because
// every request hits tables that were never created — the visible symptom is
// `42P01: relation "platform.background_jobs" does not exist` from the job processor, which looks
// like a job bug and is not one.
await app.RunPlenipoPlatformAsync();

// Required so WebApplicationFactory<Program> can host the app from the integration tests.
public partial class Program;
