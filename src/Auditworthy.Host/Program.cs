using Auditworthy.Compliance;
using Plenipo.Application.Authorization;
using Plenipo.AspNetCore.Hosting;
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

app.UsePlenipoPlatform();

app.Run();

// Required so WebApplicationFactory<Program> can host the app from the integration tests.
public partial class Program;
