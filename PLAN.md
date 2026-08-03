# PLAN — Auditworthy

**Product:** Auditworthy · **Module id:** `compliance` · **Repo:**
[abrahamFerga/auditworthy](https://github.com/abrahamFerga/auditworthy)
**Source:** [SPEC.md](SPEC.md) (accepted) · [research/regulatory-compliance.md](research/regulatory-compliance.md)

> **Verification: L2 structural completeness + L5 human acceptance.** There is no compiler for a
> plan. The coverage gate in §10 is a mechanical set comparison; everything else is judgement until a
> human accepts it. Nothing ran.

**Hard limits carried from the spec and not softened here:** v1 ships **exactly two frameworks**
(ISO 27001, NIS2), not 150. Evidence is **upload-and-attest**, never continuous automated collection.

---

## 1. Epics in build order

### Epic 1 — Controls register *(the walking skeleton)*

**There is no Foundations epic, and this is deliberate.** The backbone is `AddPlenipoPlatform()` —
one method call. Auth, tenancy, RBAC, approvals, audit, the chat transports and the admin console are
running before epic 1 starts. An epic describing any of them would be rebuilding a weaker copy of a
spine that is already correct.

Epic 1 is the thinnest *real* domain slice, and it proves four things end to end:

1. **the module loads** — `GET /api/platform/modules` lists `compliance`;
2. **a read tool answers a real domain question** — "which controls do we have and what state is each
   in?" over real control data;
3. **a write tool parks on the gate** — `propose_control_change` emits `CUSTOM(approval_required)` on
   the AG-UI turn, and **the reply does not claim the change happened**;
4. **a tab renders it** — the Controls register.

*Passes the skeleton test:* this description would not read identically for a product in another
industry. It names controls and control status, which is this domain's vocabulary.

**Delivers:** browse the controls register · inspect one control · propose a control status change.
**Depends on:** nothing.

### Epic 2 — Framework library & requirement linkage
ISO 27001 and NIS2 only. Requirements become linkable to controls, so a control can answer "what does
this satisfy?".
**Delivers:** browse the framework library.
**Depends on:** E1 (controls must exist to link to). *Why here:* every later epic reasons over
requirements; gaps and evidence are both meaningless without them.

### Epic 3 — Evidence capture & review
Upload a document, link it to a requirement, and have a second person accept or reject it as
satisfying that requirement. Both writes are gated.
**Delivers:** attach a document as evidence · accept or reject evidence.
**Depends on:** E2. *Why here:* it is the highest-risk unknown in the plan — the OCR/extraction path
and the RAG ingestion trigger both surface here, and unknowns come early.

### Epic 4 — Readiness & gap analysis
Which requirements have an effective control with accepted evidence, and which do not.
**Delivers:** gap analysis against a framework.
**Depends on:** E2, E3. *Why here:* it is a pure read over what E2 and E3 established.

### Epic 5 — Remediation tracking
A gap becomes a proposed task with an owner and a due date. Gated.
**Delivers:** propose a remediation task.
**Depends on:** E4. *Why here:* remediation without gap analysis is a to-do list.

### Epic 6 — Cited evidence Q&A *(differentiator)*
Ask a question and get an answer grounded in the client's own evidence documents, with citations
resolving to specific files.
**Depends on:** E3. *Why last-but-one:* it is a differentiator, so it must be able to slip without
blocking v1.

### Epic 7 — Auditor-ready export pack *(differentiator)*
The pack, including the **approval trail** — proposer, approver, timestamp — for every control status
and evidence acceptance.
**Depends on:** E3, E4. *Why last:* it is the differentiator that composes everything else, and it is
the one most likely to change shape after a real auditor sees it (**OQ3**).

**Not an epic:** *self-hostable* and *open source* are structural facts about the repo and the
licence. They are not work and get no issue.

## 2. Delivered by the platform — struck before grouping anything

| Capability the spec implies | Seam that already delivers it |
|---|---|
| Sign-in, SSO, users, invites | platform auth + admin console |
| Roles, permissions, a role editor | dotted permissions + runtime-editable baselines at `/admin` |
| **Audit log / who-changed-what** | the append-only audit database — already records every tool call |
| **The approval gate itself** | `RequiresApproval = true` and the approval lane |
| **Client-organisation isolation** | `ITenantOwned` + global query filters |
| File upload, PDF text extraction, **OCR** | tenant-scoped file store + platform document tools |
| **Semantic search over documents** | the RAG pipeline, per-collection gating, citations |
| Chat window, streaming, history | `/api/chat/stream`, `/api/agui/compliance`, `/hubs/agent` |
| Usage limits, token budgets, cost dashboards | per-tenant usage tracking |
| Third-party OAuth, webhook receivers | the connector SDK |
| Email / SMS / WhatsApp delivery | notification channels |
| A job scheduler | the platform background-job processor |

**Twelve strikes.** Five of them — evidence storage, OCR, semantic retrieval, the audit trail, tenant
isolation — are headline features of the incumbents in the research artifact, and none is a line of
Auditworthy's code.

## 3. Module list

| Project | Bounded context | Capabilities served |
|---|---|---|
| `Auditworthy.Compliance` | obligations, controls, evidence, remediation | all of them |

**Exactly one module, which is the default and needs no justification.** A second was considered and
rejected: frameworks/requirements and controls/evidence share foreign keys, share an audience, and
ship together, so they fail all three split tests. Two further costs make splitting actively
dangerous here — each module carries its own `DbContext` which **must declare `HasQueryFilter` per
entity** (the platform context does it by reflection; a module context does not), multiplying the
highest-consequence bug available in this codebase; and tab routes and job `Kind` values are globally
unique, so more modules means more startup-validation surface for no domain benefit.

**Splitting by layer is not a module split.** Domain / Application / Infrastructure are files inside
this one module.

## 4. Entity sketch — conceptual only; the schema is the design loop's output

All entities are `ITenantOwned`. **Confirmed for every row.**

| Entity | Fields that matter | PII |
|---|---|---|
| `Framework` | name, version, source | — |
| `Requirement` | framework, clause reference, text | — |
| `Control` | name, description, status, owner | owner is a user reference → **PII** |
| `ControlRequirement` | control ↔ requirement link, rationale | — |
| `Evidence` | document reference, title, period covered, review state, reviewed-by | **PII** — the *document contents* are uncontrolled and may contain anything |
| `RemediationTask` | title, owner, due date, status | owner → **PII** |

The `Evidence` row is the one to watch: we control the metadata schema but not what a customer
uploads inside a policy PDF.

## 5. Tool inventory

The product's real API to the agent. Each permission string appears in **two** places at build time —
the manifest `ToolDescriptor` and the `ModuleTool` — and they must agree; `GET
/api/admin/security/catalog` is where a mismatch surfaces.

| Tool | Does (what the model routes on) | Permission | Approval | Epic |
|---|---|---|---|---|
| `list_controls` | Lists the organisation's controls with their current status | `tools.compliance.list_controls` | no | 1 |
| `get_control` | Returns one control in detail, with the requirements it is linked to | `tools.compliance.get_control` | no | 1 |
| `propose_control_change` | Proposes a new status for a control, with a reason | `tools.compliance.propose_control_change` | **YES** | 1 |
| `list_frameworks` | Lists available frameworks and their requirements | `tools.compliance.list_frameworks` | no | 2 |
| `attach_evidence` | Links an uploaded document to a requirement as evidence for a stated period | `tools.compliance.attach_evidence` | **YES** | 3 |
| `review_evidence` | Accepts or rejects a piece of evidence as satisfying its requirement | `tools.compliance.review_evidence` | **YES** | 3 |
| `analyze_gaps` | Reports which requirements of a framework lack an effective control or accepted evidence | `tools.compliance.analyze_gaps` | no | 4 |
| `propose_remediation` | Proposes a remediation task for a gap, with owner and due date | `tools.compliance.propose_remediation` | **YES** | 5 |
| `ask_evidence` | Answers a question from the organisation's evidence documents, with citations | `tools.compliance.ask_evidence` | no | 6 |
| `export_audit_pack` | Generates an auditor-ready pack for a framework, including the approval trail | `tools.compliance.export_audit_pack` | no | 7 |

**Four writes, four gates.** Decided here, not discovered later. `export_audit_pack` generates and
commits nothing, so it is not gated.

## 6. Tab inventory

Routes are unique across every module in the product.

| Tab | Route | Permission | Epic |
|---|---|---|---|
| Controls | `/compliance/controls` | `compliance.view` | 1 |
| Frameworks | `/compliance/frameworks` | `compliance.view` | 2 |
| Evidence | `/compliance/evidence` | `compliance.view` | 3 |
| Readiness | `/compliance/readiness` | `compliance.view` | 4 |

## 7. Permission model

Shipped baselines, registered at the host. Roles **narrow** what RBAC allows; nothing here grants.

```
compliance-reader     chat.use, chat.conversations.view, files.read,
                      tools.documents.read_document, tools.documents.list_documents,
                      compliance.view,
                      tools.compliance.list_controls, tools.compliance.get_control,
                      tools.compliance.list_frameworks, tools.compliance.analyze_gaps,
                      tools.compliance.ask_evidence, tools.compliance.export_audit_pack

compliance-analyst    (everything above) + files.upload +
                      tools.compliance.propose_control_change,
                      tools.compliance.attach_evidence,
                      tools.compliance.propose_remediation
                      — enumerated allowlist, NOT a wildcard.
                      Deliberately EXCLUDED: tools.compliance.review_evidence,
                      and chat.approvals.manage.

compliance-owner      compliance.view, compliance.manage,
                      tools.compliance.*            (wildcard)
                      Permissions.ManageApprovals   (chat.approvals.manage)
```

**The analyst's exclusion is the load-bearing property of this whole model** and must survive every
future edit: an analyst may propose three state changes and approve none of them, and may not clear
their own parked approval. Without that, the approval lane is ceremony and the product's central
claim is false.

`system_admin` is not a product role, is never customizable, and always resolves to `*`.

## 8. Connector surface

**None for v1 — and this is a deliberate scope decision, not an omission.**

Continuous automated evidence collection is Vanta's and Drata's core product and is *N* integrations,
each one a schedule risk. v1 is upload-and-attest. The first connector is a post-v1 question, and the
right one to ask then is which single system proves the pattern end to end.

## 9. Background jobs

**None for v1.**

An evidence-expiry sweep — certificates and attestations expire, and "evidence doesn't rot in a
folder" is job J3's stated outcome — is the obvious first job. It is **not planned here** because no
SPEC must-have covers it, and inventing scope during planning is how a v1 stops shipping. Raised as
**OQ5** instead.

Any future job runs on the platform's processor. **Do not plan a scheduler.**

## 10. Coverage — the gate

One row per SPEC capability, exactly one owning epic. Zero unplaced, zero duplicated.

| SPEC capability | Owning epic |
|---|---|
| Browse the obligations/controls register | 1 |
| Inspect one control and its linked requirements | 1 |
| Propose a control status change | 1 |
| Browse the framework library | 2 |
| Attach a document as evidence for a requirement | 3 |
| Accept or reject evidence as satisfying a requirement | 3 |
| Gap analysis against a framework | 4 |
| Propose a remediation task with an owner and due date | 5 |
| Cited evidence Q&A over the client's document corpus *(differentiator)* | 6 |
| Auditor-ready export pack | 7 |
| The approval trail is part of the export *(differentiator)* | 7 |

**11 capabilities — 9 must-have + 2 differentiator — 11 rows, 0 unplaced, 0 duplicated.** Set
comparison against SPEC §4a/§4b: **empty diff.**

*Note:* "Inspect one control and its linked requirements" is owned by **E1**; E2 extends it with
requirement linkage as a **dependency edge**, not a second listing.

## 11. Open questions for the design loop

| # | Decision | Options | Decider |
|---|---|---|---|
| **OQ1** | **Load-bearing. A cross-client portfolio view may be impossible by construction.** If each client organisation is a tenant, `ITenantOwned` global query filters make a cross-tenant query impossible — the invariant working correctly, not a bug | **(a)** the advisor firm is the tenant and clients are a scoping entity inside it — isolation becomes intra-tenant and weaker; **(b)** clients stay true tenants and the portfolio view is a host-level surface reading aggregates only | `/shape:design-product` **+ a human.** Must not be settled implicitly by whoever writes the `DbContext` first |
| **OQ2** | **Is the export's approval trail ours or a platform primitive?** The pack must include proposer, approver and timestamp for every gated write — that data lives in the platform's approval and audit stores, not in our module | a module-readable query seam already exists; or it is a platform gap and needs `/deliver:request-platform-change` | `/shape:design-product`. **Asked now it costs a paragraph; discovered in epic 7 it blocks the issue** |
| OQ3 | What export format do auditors actually accept? | PDF pack, structured export, or per-framework | **a real buyer — L3, and months away.** Do not let epic 7 harden around a guess |
| OQ4 | RAG collection scoping for evidence | per client, per framework, or per control | `/shape:design-product`; research §7 suggests per client, but citation precision may want narrower |
| OQ5 | Is an evidence-expiry sweep in v1? | yes (adds the first background job) / no (post-v1) | a human, next planning round |
| OQ6 | How are framework definitions carried? | seeded data, a file format, or an importable library | `/shape:design-product`. Decides whether "ship 2, add more later" is cheap or a rewrite |

---

## Exit condition

| Gate | Result |
|---|---|
| **Coverage** — every SPEC must-have owned by exactly one epic, none unplaced, none duplicated | **pass** — §10, 11 rows, empty diff |
| **Skeleton** — epic 1 names a domain capability and lists the four proofs; no epic describes auth, tenancy, audit, approvals-as-mechanism, a scheduler, a chat panel, or a connector registry | **pass** — epic 1 is the controls register; all twelve platform capabilities are struck in §2 |

**Terminal state: `Success`** on the L2 structural gates. **L5 acceptance by a human is outstanding**
and the backlog should not be published before it.

**One module, no connectors, no background jobs, no platform changes** — so this plan does **not**
require `Approval-required` on structural grounds.
