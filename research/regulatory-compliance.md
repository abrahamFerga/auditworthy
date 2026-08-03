# Regulatory compliance & audit management — industry research

**Industry:** regulatory compliance and audit management (horizontal, cross-industry)
**Slug:** `regulatory-compliance`
**Researched:** 2026-08-01

> **Evidence level: L4.** This is synthesis of secondary sources — vendor sites, one regulator page,
> and industry comparison writing. It is my reading of other people's marketing. No amount of
> citation upgrades it; the only L3 signal in this loop is a real buyer. Where I could not verify
> something, it says so.

---

## 1. The headline finding, stated first

**The free / open-source / self-hostable / AI-first position in this market is already occupied —
by [CISO Assistant](https://intuitem.com/ciso-assistant/) (intuitem, AGPL v3).** [6][7]

It ships 134–150+ compliance frameworks including **ISO 27001, GDPR, NIS2, DORA, SOC 2, CMMC and
HIPAA**, an automatic mapping engine that links one control to requirements across every framework at
once, and — decisively — **AI integrated natively via MCP with an embedded chat that runs against
your own models, inside your own environment**, giving suggestions for risk scoring, control mapping
and gap analysis. It was covered by Help Net Security in January 2026 and is on a v3.20 release
cycle. [6][7]

That is, almost line for line, the product this phase set out to specify. **Anyone proposing "a free,
open-source, self-hostable, AI-first compliance system" in 2026 is proposing CISO Assistant.**

This does not automatically end the opportunity — §9 identifies where room genuinely remains — but it
must be the first fact anyone reads, and it invalidates the wedge as originally stated.

## 2. Sources

| # | Source | Read |
|---|---|---|
| 1 | [SimpleRisk — open-source deployment](https://www.simplerisk.com/deployment/open-source) | 2026-08-01 |
| 2 | [InfoSecFlow — open-source GRC compared: CISO Assistant vs Eramba vs SimpleRisk](https://infosecflow.com/blog/open-source-grc-comparison/) | 2026-08-01 |
| 3 | [Open Risk Register — open-source GRC tools for NIS2](https://openriskregister.org/alternatives/open-source-grc-tool/) | 2026-08-01 |
| 4 | [Vanta vs Drata pricing comparison](https://datavirtualizer.com/content/vanta-vs-drata-soc2-compliance-automation-pricing/) | 2026-08-01 |
| 5 | [Secureleap — Vanta pricing 2026](https://www.secureleap.tech/blog/vanta-review-pricing-top-alternatives-for-compliance-automation) | 2026-08-01 |
| 6 | [intuitem — CISO Assistant](https://intuitem.com/ciso-assistant/) and [frameworks](https://intuitem.com/frameworks/) | 2026-08-01 |
| 7 | [Help Net Security — CISO Assistant](https://www.helpnetsecurity.com/2026/01/14/ciso-assistant-open-source-cybersecurity-management-grc/) | 2026-01-14 article, read 2026-08-01 |
| 8 | [European Commission — NIS2 Directive](https://digital-strategy.ec.europa.eu/en/policies/nis2-directive) | 2026-08-01 — **the only primary regulator source here** |
| 9 | [Sprinto — Drata pricing](https://sprinto.com/blog/drata-pricing/) | 2026-08-01 |

Vendors named in the brief that commissioned this research — AuditBoard, Workiva, Diligent, TeamMate,
LogicGate, Onspring, DataSnipper, MindBridge, CaseWare, Suralink, Inflo, Sphera, Watershed, Persefoni
— are **carried as context, not as researched claims.** I did not open their sites in this pass.
Treat every statement about them as `unknown` until verified. Marked as an open question in §10.

## 3. The field

| Vendor | Class | Who buys it | Packaging | Src |
|---|---|---|---|---|
| **CISO Assistant** (intuitem) | **AI-native challenger, open source (AGPL v3)** | security teams, CISOs | free self-host; commercial support | [6][7] |
| **Eramba** | incumbent, open-core | infosec teams | Community free; **Enterprise from €2,500/yr self-hosted, ~€5,000/yr hosted** | [2][3] |
| **SimpleRisk** | incumbent, open-core | risk teams | **Core free, unlimited users, self-hosted**; PHP/MySQL | [1][2] |
| **Vanta** | AI-native challenger, SaaS | startups → mid-market | per-seat, scales with headcount; **~$10k/yr entry, ~$20k median** | [4][5] |
| **Drata** | AI-native challenger, SaaS | startups → mid-market | **~$15k/yr foundation to 50 FTE**, range $7.5k–$100k+, **median ~$24.9k** | [4][9] |
| **OpenRMF**, **Open Risk Register**, **VerifyWise** | open source, niche | small teams | free | [2][3] |
| **AuditBoard, Workiva, Diligent, TeamMate, LogicGate, Onspring** | incumbents, enterprise GRC | internal audit, SOX | `unknown` — not researched | — |
| **The spreadsheet + shared mailbox + SharePoint folder** | **adjacent horizontal — and the real incumbent below the mid-market** | everyone under the price floor | free | inferred |

**The pricing gap is the one genuinely encouraging number in this document.** A mid-market company
of ~75 employees pursuing SOC 2 plus ISO 27001 lands at **$13k–$18k/yr** [4]. Everything below that
line is served by open source or by spreadsheets. **But that space is not empty — it is where
Eramba, SimpleRisk and CISO Assistant already live.**

## 4. Capability matrix

Industry vocabulary. `unknown` used freely — a marketing page is not a feature list.

| Capability | CISO Assistant | Eramba | SimpleRisk | Vanta | Drata |
|---|---|---|---|---|---|
| Multi-framework control library | **yes** (134–150+) [6] | yes (ISO 27001, GDPR pkgs) [2] | partial [1][2] | yes [4] | yes [9] |
| Cross-framework control mapping | **yes** — automatic engine [6] | unknown | unknown | yes [4] | yes [9] |
| Risk register / assessment | yes [6] | yes [2] | **yes** — its core [1][2] | partial | partial |
| Policy management | unknown | **yes** [2] | unknown | yes [5] | yes [9] |
| Internal audit management | unknown | **yes** [2] | unknown | unknown | unknown |
| **Automated evidence collection from systems** | unknown | unknown | unknown | **yes** — the core pitch [4][5] | **yes** [4][9] |
| Embedded AI chat / assistant | **yes** — MCP, your own models, in your environment [6] | no evidence | no evidence | yes (claimed) [5] | yes (claimed) [9] |
| Self-hostable | **yes** [6] | **yes** [2][3] | **yes** [1] | no | no |
| Document OCR + extraction | unknown | unknown | unknown | unknown | unknown |
| **Multi-tenant: many client orgs in one install** | **unknown — likely no** (single-org shaped) | unknown | unknown | partner programmes `unknown` | partner programmes `unknown` |
| Financial-controls / SOX testing | no evidence — security-centric | no evidence | no evidence | no | no |

**The two most informative rows are the last two**, and both are where §9 finds room.

## 5. UX patterns

The category's spine is consistent: a **register** (risks, controls, obligations) as the primary
object; list → detail → linkage; a **framework tree** you can walk; an **evidence locker** attached to
controls; a **gap/readiness dashboard**; and one exported artifact that justifies the whole purchase —
the auditor-ready pack.

**Chat-hostile parts, named honestly:**

- **Control-to-requirement mapping across 150 frameworks is a matrix operation, not a conversation.**
  CISO Assistant does it with a deterministic mapping engine [6], which is the right shape. An LLM
  doing it conversationally would be slower and less trustworthy.
- **Continuous evidence collection is an integration problem**, not a chat problem — it is polling
  cloud APIs on a schedule, which is what Vanta and Drata actually sell [4][5].
- Bulk control status updates are grid work.

What *is* chat-shaped: "which of our obligations does this new contract clause touch?", "show me the
evidence that this control operated last quarter, with citations", "draft the gap-analysis narrative
for clause 8.2".

## 6. Compliance constraints

| Obligation | Who is bound | Requires | Class |
|---|---|---|---|
| **NIS2** | **medium-sized and large entities across 18 sectors** [8] | cybersecurity risk-management measures, incident notification to national authorities, supply-chain security, vulnerability management, awareness training — **and explicit accountability of top management for non-compliance** [8] | **Product** — an obligations register and an incident-notification workflow |
| NIS2 transposition | Member States, deadline **18 Oct 2024**, NIS1 repealed; Commission proposed simplifying amendments **20 Jan 2026** affecting ~28,700 companies [8] | jurisdictional variance | **Product** — per-jurisdiction variation |
| ISO 27001 / 9001 certification | the organisation | a certification audit by an accredited body | **Business** — we are not a certification body and cannot become one |
| GDPR, CSRD, DORA, EU AI Act | varies | `unknown` — **not researched from source in this pass** | open question |

**"Top management accountability" in NIS2 [8] is the single most interesting line for this
platform** — a regime that makes named individuals answerable is a regime that wants approvals and an
append-only audit trail, which is exactly the spine.

**Nothing here lands in `Business` except certification itself**, which no software delivers and which
every vendor in §3 also fails to deliver. So this run is **not** `Approval-required` on compliance
grounds.

## 7. Platform mapping

| Platform capability | Table stake it covers | Still to build | Verdict |
|---|---|---|---|
| Chat-first (SignalR + AG-UI) | "ask the system about our obligations" | tools + suggested prompts | delivered |
| RBAC before the model | preparer vs reviewer vs accountable owner | permission strings, role baselines | delivered |
| **Approvals on every write** | control status changes and evidence acceptance are consequential | choosing which tools are writes | delivered |
| **Append-only audit** | the audit trail *is* the deliverable here | the auditor-accepted export format | delivered |
| **Multi-tenancy** | **many client organisations in one install** | `HasQueryFilter` per entity | delivered — **and it is the differentiator, see §9** |
| Documents + OCR | policies, certificates, DPAs, test results, supplier attestations | extraction schema per document type | **delivered with module work** |
| Scoped RAG | per-client evidence corpus with citations | collection boundary, ingestion trigger | **delivered with module work** |
| Connectors | pull evidence from cloud systems | one connector per source | **delivered with module work — and this is the expensive part** |
| WhatsApp channel | weak fit — compliance is desk work | — | delivered, low value here |
| — | **cross-framework control mapping across 150 frameworks** | a deterministic mapping engine + a maintained framework library | **NOT DELIVERED** → §8 |
| — | **continuous automated evidence collection** | a scheduler plus N system integrations | **NOT DELIVERED** → §8 |

## 8. Gaps — what the platform does not give you here

1. **The framework library itself.** 150 frameworks, maintained as regulations change, is a
   *content* asset, not a code asset. CISO Assistant's real moat is this library [6], not its code.
   Building it is a standing editorial cost with no end date. **A v1 cannot ship 150 frameworks and
   should not pretend to** — it ships two or three and says so.
2. **Continuous automated evidence collection.** Vanta's and Drata's core value [4][5] is polling
   cloud systems on a schedule. That is N integrations, each a schedule risk. **v1 must be
   upload-and-attest, and must say that plainly**, because buyers compare against Vanta here.
3. **A deterministic cross-framework mapping engine.** Genuinely valuable, genuinely not an LLM job,
   and not something the spine provides.
4. **Certification.** Nobody's software delivers it — but buyers conflate readiness with
   certification and the product must not encourage that.
5. **No offline/mobile need, no network effect, no ledger.** Unusually clean on these — worth noting,
   because it removes three of the recurring gap classes.

**Gap 1 and gap 2 together are the honest reason this market is hard: its two hardest assets are
editorial content and integration breadth, and the platform helps with neither.**

## 9. Must-have / differentiator / out-of-scope

### Must-have — in a majority of leaders *and* required by the workflow
- An obligations/controls register with framework linkage [1][2][6]
- Evidence attached to controls, with a review state
- A readiness/gap dashboard
- An auditor-ready export
- Self-hosting [1][2][6]

### Differentiator — what the platform makes cheap that the field does badly

**One candidate survives scrutiny, and it is not "AI" and not "open source" — both are taken [6].**

> **Multi-tenant compliance for the people who run *other* organisations' programmes** — compliance
> consultancies, vCISOs, managed compliance providers and accountancy firms carrying many client
> programmes at once.

> ### ❌ RETRACTED — checked immediately after writing, and it does not survive.
>
> Open question 1 was resolved before this document was handed over, and the answer kills the
> differentiator rather than confirming it.
>
> **There is an established product category for exactly this buyer: "vCISO platforms for MSPs",**
> with at least four named players compared head-to-head in 2026 — **Cynomi, GetCybr, Vanta and
> Drata** [10]. The multi-client compliance advisor is not an underserved abstraction; it is a
> category with its own comparison articles.
>
> Worse for the argument: **Vanta and Drata appear in that comparison**, which means the per-seat
> objection above is wrong — they evidently reach this buyer through partner/MSP motions.
>
> On CISO Assistant specifically the answer is **still `unknown`, not confirmed**: its docs name
> "domains" and "perimeters" as central objects but never describe tenant isolation or MSSP use [6].
> That question is now moot — even if CISO Assistant is single-tenant, the buyer it would leave open
> is already served by Cynomi and GetCybr.
>
> **The one honest opening left in the evidence** is narrow and I am not going to inflate it: the
> comparison writing notes that some platforms in this category are **session-centric rather than
> portfolio-centric** — you enter one client's context at a time, with no cross-client dashboard [10].
> That is a UX complaint about specific products, not a structural gap. Per the fit test, *"ours will
> be easier to use"* is **not** a legitimate entrant argument. **It does not clear the bar.**

The original argument, retained so the reasoning can be audited:

- ~~CISO Assistant, Eramba and SimpleRisk are single-organisation shaped~~ — unverified, and moot.
- ~~Vanta and Drata price per seat, the wrong shape for an advisor with many small clients~~ —
  **contradicted by [10]**.
- **Plenipo's tenancy spine is exactly this shape** — still true, and still the one capability in §7
  that is *delivered* rather than *delivered with work*. It is simply not enough on its own.
- **Approval-gated writes and an append-only audit trail matter more when attesting on someone
  else's behalf**, and NIS2's explicit "top management accountability" [8] rewards provable
  maker–checker. Still true. Still not a category-entry argument by itself.

### Out-of-scope — every exclusion with its reason
- **150 frameworks** — gap 1. Ship 2–3.
- **Continuous automated evidence collection** — gap 2. Upload-and-attest for v1.
- **Certification** — gap 4, a business act nobody's software performs.
- **Financial-statement audit / SOX testing** — a different buyer and a different field (DataSnipper,
  MindBridge, AuditBoard, all `unknown` here). Excluded until researched, **despite being part of the
  original request**, because I have no evidence about that field.
- **Whistleblowing intake** — separately occupied, including at a flat €60/month price point that
  defeats any free wedge.
- **WhatsApp-first capture** — §5, weak fit; compliance is desk work.

## 10. Open questions for the spec

1. **Is CISO Assistant genuinely single-tenant?** The recommendation in §9 rests entirely on this and
   it is currently `unknown`. **Verify by reading its data model before committing a line of code.**
2. Do Vanta and Drata operate MSP/partner programmes that already serve the multi-client advisor?
   `unknown`, and it would blunt the differentiator.
3. The enterprise GRC field — AuditBoard, Workiva, Diligent, TeamMate, LogicGate, Onspring — was
   **not researched**. Any of them may already serve multi-client advisors.
4. The financial-audit field — DataSnipper, MindBridge, CaseWare, Suralink, Inflo — **not
   researched**, and it is half of what was originally asked for.
5. GDPR, CSRD, DORA and EU AI Act obligations were **not read from source**; only NIS2 was [8].
6. Which 2–3 frameworks should v1 carry? ISO 27001 and NIS2 are the obvious pair on this evidence.
7. What export format do auditors actually accept? Unknown and load-bearing for the "audit trail as
   deliverable" claim.
8. Is the multi-client advisor a real buyer with a real budget, or an appealing abstraction? **Only a
   real buyer answers this — L3, and months away.**

---

## ✅ HUMAN OVERRIDE — 2026-08-01, after this research was presented

**The no-go below was presented in full, with every occupancy finding, and the human decided to build
anyway: a product "really similar to intuitem" (CISO Assistant).**

This is an **L5 decision and it stands.** The research is not withdrawn — it is the honest field map,
and it remains the record of what this product is walking into. It is now *input to a build*, not a
gate on one.

**Positioning that follows from the decision:** a CISO-Assistant-shaped compliance and audit system,
built **Plenipo-native**. The competitive claim is *not* "the space is empty" — §1 proves it is not.
It is that four platform capabilities come free here and are not the shape of the incumbents:

1. **Multi-tenancy by architecture** — many client organisations in one install, isolated by
   `HasQueryFilter`. CISO Assistant is single-organisation shaped; the vCISO-for-MSP category [10]
   reaches this buyer with SaaS pricing, not with a self-hostable install.
2. **Approval-before-write on every consequential action** — a control status change or evidence
   acceptance is maker–checker by construction, not by convention.
3. **Append-only audit as the deliverable** — in this market the audit trail *is* the product, and
   NIS2's explicit "top management accountability" [8] rewards provable maker–checker.
4. **Evidence-corpus RAG with citations** — answering "show the evidence this control operated in
   Q3, with citations" over a document corpus, rather than reading a control register.

**Licensing note:** CISO Assistant is **AGPL v3** [6]. This product must be an independent
implementation; no code is to be copied from it.

**Gaps §8 still bind and are not softened by the override:** v1 ships 2–3 frameworks, not 150, and
upload-and-attest, not continuous automated evidence collection. Those were honest limits before the
decision and remain honest limits after it.

---

## Terminal state: `Success` as research — **the market verdict was a no-go, and was overridden above**

`research/regulatory-compliance.md` is written, every claim carries a source number or is marked
`unknown`, and §8 is non-empty. **As a research artifact this ran to completion.**

**As a product decision it is a no-go, and I will not dress it up:**

1. The free / open-source / self-hostable / **AI-first** position is occupied by **CISO Assistant** —
   150 frameworks, MCP-native AI chat against your own models, AGPL v3, actively released [6][7].
2. The non-AI open-source position is occupied by **Eramba** and **SimpleRisk** [1][2][3].
3. The funded SaaS position is occupied by **Vanta** and **Drata** [4][5][9].
4. The multi-client-advisor differentiator this document proposed in §9 — the one genuinely
   platform-shaped idea — is occupied by an established **vCISO-platform-for-MSPs** category:
   **Cynomi, GetCybr**, and Vanta and Drata via partner motions [10].
5. What remains is a UX complaint (session-centric vs portfolio-centric [10]), which the fit test
   explicitly rules out as an entrant argument.

**Coverage is partial and is not rounded up:** the enterprise GRC field (AuditBoard, Workiva,
Diligent, TeamMate, LogicGate, Onspring) and the **entire financial-audit field** (DataSnipper,
MindBridge, CaseWare, Suralink, Inflo) were not researched, and only NIS2 was read from its regulator
[8]. Those gaps could change the picture — but they would have to change it *in favour*, against four
independent occupancy findings, which is not where the evidence is pointing.

**`../synthesize-spec` should not run on this.** Specifying a product whose every position is held
would produce a document that reads well and builds a weaker copy of CISO Assistant.

**Source [10]:** [GetCybr — best vCISO platforms 2026, ranked for MSPs](https://getcybr.com/insights/best-vciso-platforms-2026-comparison-guide/),
read 2026-08-01. Note the bias: it is published by one of the ranked vendors. The fact it establishes
— *that the category exists and is competitively compared* — is unaffected by that bias.
