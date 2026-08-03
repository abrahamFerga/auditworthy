# SPEC — compliance & audit management (Plenipo-native)

> **Product: `Auditworthy`** · **Module id: `compliance`** · **Repo:
> [abrahamFerga/auditworthy](https://github.com/abrahamFerga/auditworthy)** (public, created
> 2026-08-03).
>
> Chosen by a human on 2026-08-03 against two stated criteria — the `.ai` domain must be free and the
> name must not overlap an established product. Verified by RDAP and search: **auditworthy.ai is
> unregistered** ("Object not found"), no product or GitHub org uses the name. Rejected on evidence:
> **auditwell** (Datamethod's *AuditWell*, a shipping transaction-audit platform), **proofwell** /
> **attestwell** / **complywell** (`.ai` already registered), **attestly** and **conformally** (live
> compliance-software companies), **provewell** (PROVEWELL LIMITED, a UK company).
> **Known caveat, recorded rather than hidden:** `auditworthy.com` was registered 2026-07-25, nine
> days before this repo, with nothing published on it.
>
> Every permission string below resolves as `tools.compliance.<tool>`.

**Source:** [`research/regulatory-compliance.md`](research/regulatory-compliance.md). That artifact
concluded **no-go**; a human saw every occupancy finding and decided to build anyway, wanting a
product similar to intuitem's CISO Assistant. That decision is **L5 and stands** and is not
relitigated here.

**Evidence level: L4** — this spec is a judgement until a human accepts it. The exit check at the end
is an **L2 rule check on a document**, not a test run.

**Licensing constraint:** CISO Assistant is **AGPL v3**. This is an independent implementation. No
code is copied from it.

---

## 1. Framing

> **`Auditworthy` lets a compliance lead run an organisation's obligations, controls and evidence out
> of the documents they already hold — with an accountable owner approving before any control status
> or evidence acceptance is committed.**

**The approval-worthy write:** marking a control *effective*, and accepting a document as evidence
that a requirement is satisfied. Both are assertions someone will later rely on in front of an
auditor or a regulator. An AI proposing them is valuable; an AI committing them unreviewed is the
whole liability.

## 2. Jobs to be done

| # | Job | Observable outcome |
|---|---|---|
| J1 | When a new framework applies to us, I want to see which existing controls already satisfy it and where the gaps are, so I can plan remediation | a gap list an auditor could read, per requirement |
| J2 | When an auditor asks "show me the evidence this control operated in Q3", I want a cited answer, so I don't hunt through folders | an answer citing specific source documents |
| J3 | When someone uploads a policy, certificate or attestation, I want it linked to the requirements it satisfies and reviewed, so evidence doesn't rot in a folder | evidence carrying a review state and a named approver |
| J4 | When a control's status changes, I want it to require a second person and be permanently recorded, so accountability is provable | an append-only entry naming proposer, approver and time |
| J5 | When I run programmes for several client organisations, I want to work across them without their data ever mixing | per-client isolation enforced below the query layer |

## 3. Personas and authority tiers

| Persona | May read | May draft / propose | May commit / approve |
|---|---|---|---|
| **Auditor / executive** | everything in their client | — | — |
| **Compliance analyst** | everything in their client | control changes, evidence links, remediation | **nothing** |
| **Accountable owner** | everything in their client | all of the above | control status, evidence acceptance, and clearing a parked approval |

**Merges made now, deliberately:** "evidence contributor" and "compliance analyst" differed only in
what they could attach — same read, same approve (none). **One role.** Likewise "compliance manager"
and "risk owner" collapse into **accountable owner**: identical on all three columns.

Three roles, not six. The analyst is the tier that **proposes but cannot approve** — without it the
approval lane is ceremony.

## 4. Capabilities

### 4a. Must-have — table stakes, per research §9

| Capability | Seam | Approval-gated | Permission | Job |
|---|---|---|---|---|
| Browse the obligations/controls register | **Tool** `list_controls` + **Tab** *Controls* | no (read) | `tools.compliance.list_controls` | J1 |
| Inspect one control and its linked requirements | **Tool** `get_control` + Tab | no (read) | `tools.compliance.get_control` | J1 |
| **Propose a control status change** | **Tool** `propose_control_change` | **YES** | `tools.compliance.propose_control_change` | J4 |
| **Attach a document as evidence for a requirement** | **Tool** `attach_evidence` | **YES** | `tools.compliance.attach_evidence` | J3 |
| **Accept or reject evidence as satisfying a requirement** | **Tool** `review_evidence` | **YES** | `tools.compliance.review_evidence` | J3 |
| Gap analysis against a framework | **Tool** `analyze_gaps` + **Tab** *Readiness* | no (read) | `tools.compliance.analyze_gaps` | J1 |
| Browse the framework library | **Tab** *Frameworks* | no (read) | `tools.compliance.list_frameworks` | J1 |
| **Propose a remediation task with an owner and due date** | **Tool** `propose_remediation` | **YES** | `tools.compliance.propose_remediation` | J1 |
| Auditor-ready export pack | **Tool** `export_audit_pack` + Tab | no (generates, commits nothing) | `tools.compliance.export_audit_pack` | J2 |

### 4b. Differentiators — two, and both are structural rather than cosmetic

| Capability | Seam | Approval-gated | Permission | Why the incumbents don't have this shape |
|---|---|---|---|---|
| **Cited evidence Q&A over the client's own document corpus** | **Tool** `ask_evidence` | no (read) | `tools.compliance.ask_evidence` | The incumbents are **control registers**. This answers J2 from the *documents*, with citations resolving to specific files — a different object model, not a nicer UI |
| **The approval trail is part of the export** — every control status and evidence acceptance carries proposer, approver and timestamp, and the audit pack includes them | **Tool** `export_audit_pack` (above) + the platform approval lane | n/a | Maker–checker is **by construction**, not by convention. NIS2 makes top management personally accountable [research §6], which is precisely a regime that wants provable two-person integrity |

**Multi-tenancy is deliberately *not* listed as a differentiator capability** — it is platform-provided
(§5), and the part of it that would be a genuine differentiator has an unresolved architectural
question. See **OQ1**.

### 4c. Out of scope for v1

| Excluded | Reason | What would reopen it |
|---|---|---|
| **A 150-framework library** | research §8 gap 1 — an editorial asset with no end date, and CISO Assistant's actual moat. **v1 ships two: ISO 27001 and NIS2** | a maintained import path, or a community contributing framework definitions |
| **Continuous automated evidence collection** | research §8 gap 2 — N integrations, and it is Vanta's and Drata's core. **v1 is upload-and-attest** | one connector proving the pattern end to end |
| **Certification** | a business act. No vendor's software delivers it | never — but the UI must not imply readiness is certification |
| **Financial-statement / SOX audit** | a different buyer, and a field this research never opened (DataSnipper, MindBridge, CaseWare, Suralink, Inflo all `unknown`) | that field being researched properly |
| **Whistleblowing intake** | separately occupied, including a flat €60/month competitor | a buyer asking for it inside an existing programme |
| **WhatsApp-first capture** | research §5 — compliance is desk work | field-evidence capture turning out to matter |
| Cross-framework mapping across many frameworks | research §8 gap 3 — a deterministic engine, not an LLM job, and not spine-provided | more than three frameworks shipping |

## 5. Platform-provided — cut here so it cannot return as a feature request

Every one of these appeared in the research capability matrix. **None is work.**

| Cut | What actually provides it |
|---|---|
| Sign-in, SSO, user management, invites | platform auth + admin console |
| Roles, permissions, a role editor | dotted permissions + runtime-editable baselines at `/admin` |
| **Audit log / "who changed what"** | the append-only audit database, already recording every tool call |
| "Are you sure?" confirmation on AI actions | `RequiresApproval = true` — the approval lane |
| **Tenant / client-organisation separation** | `ITenantOwned` + global query filters |
| File upload, PDF text extraction, **OCR** | tenant-scoped file store + platform document tools |
| **Semantic search over the client's documents** | the opt-in RAG pipeline, per-collection gating, citations |
| Chat window, streaming, history | `/api/chat/stream`, `/api/agui/{moduleId}`, `/hubs/agent` |
| Usage limits, token budgets, cost dashboards | per-tenant usage tracking |
| Third-party OAuth, webhook receivers | the connector SDK |
| Email / SMS / WhatsApp delivery | notification channels |

**Note how much of this market's table stakes is already floor.** Evidence storage, OCR, semantic
retrieval, the audit trail and tenant isolation are five of the incumbents' headline features and
none of them is a line of this product's code.

## 6. RBAC model

Shipped baselines registered at the host with `AddPlenipoRole(...)`, **runtime-editable per tenant**.
What follows is the *starting* baseline, not immutable policy. Pattern follows the reference product
(`networthy/src/Networthy.Host/Program.cs:48-78`).

```
compliance-reader    chat.use, chat.conversations.view, files.read,
                     tools.documents.read_document, tools.documents.list_documents,
                     tools.compliance.list_controls, tools.compliance.get_control,
                     tools.compliance.list_frameworks, tools.compliance.analyze_gaps,
                     tools.compliance.ask_evidence, tools.compliance.export_audit_pack

compliance-analyst   everything above, plus files.upload and the proposing writes:
                     tools.compliance.propose_control_change,
                     tools.compliance.attach_evidence,
                     tools.compliance.propose_remediation
                     — and NOT chat.approvals.manage, and NOT review_evidence

compliance-owner     tools.compliance.*  (wildcard: every tool this module exposes)
                     plus Permissions.ManageApprovals (chat.approvals.manage)
```

### May call / may approve

| Capability | reader | analyst | owner |
|---|---|---|---|
| Read register, gaps, cited Q&A, export | call | call | call |
| `propose_control_change` | — | **call, cannot approve** | call + approve |
| `attach_evidence` | — | **call, cannot approve** | call + approve |
| `propose_remediation` | — | **call, cannot approve** | call + approve |
| `review_evidence` | — | — | call + approve |
| Clear a parked approval | — | — | approve |

**`compliance-analyst` satisfies exit check 5**: it may call three state-changing capabilities and may
approve none. It is excluded from `chat.approvals.manage` on purpose — an analyst must not clear their
own gate.

`system_admin` is not respecified; it is never customizable and always resolves to `*`.

## 7. Regulatory constraints — supported vs. delivered

| Regime | Obligation | Platform **supports** | Platform does **not deliver** | Seam |
|---|---|---|---|---|
| **NIS2** [research §6, src 8] | risk-management measures; incident notification to national authorities; supply-chain security; **top-management accountability** | append-only audit; RBAC before the model; approval-before-write giving provable two-person integrity | the actual notification to a national authority — a business process with a legal deadline; and the accountability itself, which attaches to named humans | controls register + approval lane |
| **ISO 27001 / 9001** | certification by an accredited body | evidence organisation, gap analysis, the auditor-ready pack | **certification.** We are not, and cannot become, a certification body | export pack |
| GDPR, CSRD, DORA, EU AI Act | not read from source in this pass | — | — | **open question — see OQ5** |

**Nothing in the "does not deliver" column is depended on by a must-have**, so this spec does **not**
end `Approval-required`. The one thing to police is UI language: readiness must never be presented
as certification.

## 8. Success metrics

| Metric | Instrument | Target | By |
|---|---|---|---|
| **Is the agent right** — the strongest signal | approval accept/reject rate on the approval lane | ≥70% of proposed control changes accepted without edit | 60 days after first tenant |
| Time-to-decision on a parked approval | approval lane timestamps | median < 24h | 60 days |
| Is the agent used at all | `GET /api/admin/audit/tool-calls` | ≥20 tool calls/tenant/week | 30 days |
| Retrieval quality | citation rate on `ask_evidence` answers | ≥90% of answers carry ≥1 citation | 30 days |
| Cost per tenant | `GET /api/admin/usage?days=30` | within the per-tenant budget, no overage | continuous |
| Export actually used | `export_audit_pack` counter | ≥1 per tenant per quarter | first quarter |

## 9. Open questions for the shape loop

**OQ1 — the load-bearing one. A cross-client portfolio view may violate tenant isolation.**
The advisor differentiator wants one screen showing compliance status across all client
organisations. But if each client is a tenant, `ITenantOwned` global query filters make a cross-tenant
query *impossible by construction* — which is the invariant working correctly, not a bug. Two shapes
exist and the shape loop must choose: **(a)** the advisor firm is the tenant and client organisations
are a scoping entity *within* it — isolation becomes intra-tenant and weaker; **(b)** each client is a
true tenant and the portfolio view is a separate host-level surface reading aggregates only. **Do not
let this be decided implicitly by whoever writes the DbContext first.**

**OQ2** — Framework definitions: seeded data, a file format, or an importable library? Determines
whether "ship 2, add more later" is cheap or a rewrite.

**OQ3** — What export format do auditors actually accept? Load-bearing for the "audit trail as
deliverable" claim and currently unknown.

**OQ4** — Should the evidence RAG collection be scoped per control, per framework, or per client?
Research §7 says per-client is the natural boundary, but per-control citation precision may want
narrower.

**OQ5** — GDPR, CSRD, DORA and EU AI Act obligations were never read from source. Only NIS2 was.

**OQ6** — **Product name and module id.** Blocking phase 4. A human supplies both.

---

## Exit check — **L2, a rule check on a document, not a test run**

| # | Rule | Result |
|---|---|---|
| 1 | Every must-have names exactly one primary seam, none *Platform-provided* | **pass** — 9 must-haves, each a Tool and/or Tab |
| 2 | Every state-changing capability is approval-gated | **pass** — all four writes gated; reads and the export are not writes |
| 3 | Every freebie from the research matrix appears under Platform-provided | **pass** — §5, eleven entries |
| 4 | Permission strings dotted, lowercase, `tools.compliance.<tool>`; no parallel authz concept | **pass** — resolves once the module id is chosen |
| 5 | At least one role may call a state-changing capability and may not approve it | **pass** — `compliance-analyst` |

**Terminal state: `Success`** on the exit check, with **OQ6 blocking phase 4**. A human accepting this
spec is **L5** and is the only signal that closes this loop.
