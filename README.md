<div align="center">

# ERP MCP Gateway

**Give AI agents safe, audited access to your ERP: role-based tools, masked data, human approval for every write.**

A .NET 10 [Model Context Protocol](https://modelcontextprotocol.io) server plus an Angular 20 Controller Console.<br>
Claude, Copilot, Cursor and other MCP clients can answer finance questions, while people stay in control of what changes.

[![CI](https://github.com/yasirmalik36/erp-mcp-gateway/actions/workflows/ci.yml/badge.svg)](https://github.com/yasirmalik36/erp-mcp-gateway/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![Angular 20](https://img.shields.io/badge/Angular-20-DD0031?logo=angular)
![MCP 2025-06-18](https://img.shields.io/badge/MCP-2025--06--18-22D3EE)
![Runtime deps](https://img.shields.io/badge/runtime%20dependencies-0-success)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

<img src="docs/demo.gif" alt="Demo: an agent queries overdue invoices, is blocked from drafting as a Viewer, drafts a write-off as an Accountant, a human approves it, and the audit trail records every step" width="860">

</div>

---

## Why this exists

Connecting an LLM straight to a finance system is easy. Doing it so a CFO, an auditor and a security team all sign off is the hard part.
This project shows one way to do that: the AI gets **useful read access and a way to propose changes**, never the power to make them on its own.

| Risk when AI touches an ERP | How the gateway handles it |
|---|---|
| Agent sees or does more than the user should | **Least-privilege tools per role.** `tools/list` only shows what a role may use, and every call is re-checked server-side. |
| Bank details or contact data leak into a chat | **Field-level masking.** IBANs are masked below Controller and emails below Accountant (`AE07 •••• •••• 3456`). |
| Agent posts a bad or malicious journal entry | **Human-in-the-loop.** Agents can only *draft*. A person approves in the console, and nobody can approve their own draft (segregation of duties). |
| "Who let the AI do that?" | **Tamper-evident audit log.** Every call is SHA-256 hash-chained; changing any past record breaks verification. |
| Prompt injection hidden in business data | **Untrusted-data envelope.** ERP records come back inside a labelled `data` field with an explicit notice. Invoice `INV-1007` ships with a planted attack so you can test it. |
| Runaway or abusive agents | Per-caller **rate limiting**, strict **input validation** (types, ranges, lengths), payload caps, and **Origin checks** on HTTP against DNS rebinding. |
| Supply-chain risk in a security component | The MCP layer is written on the .NET base library: **zero third-party runtime packages**. |

## Architecture

```mermaid
flowchart LR
    subgraph Clients["AI clients"]
        A1[Claude Desktop]
        A2[Cursor / VS Code]
        A3[MCP Inspector]
    end
    subgraph Gateway["ERP MCP Gateway · .NET 10"]
        T1[stdio transport]
        T2["HTTP /mcp<br/>Bearer token · Origin check"]
        P["MCP protocol<br/>JSON-RPC 2.0"]
        G["Policy pipeline<br/>rate limit → RBAC → validate → mask → envelope"]
        AU[("Hash-chained<br/>audit log")]
        L[("Sample ERP<br/>customers · invoices · journal")]
    end
    H["👤 Controller Console<br/>Angular 20"]

    A1 & A2 & A3 -->|"stdio"| T1
    A1 & A2 -->|"HTTPS"| T2
    T1 & T2 --> P --> G
    G --> L
    G --> AU
    H -->|"approve / reject drafts<br/>view audit trail"| L
    H --> AU
```

```
src/
  ErpMcp.Domain     Ledger, invoices, journal entries, segregation of duties (no framework code)
  ErpMcp.Protocol   Dependency-free MCP server: initialize, ping, tools/list, tools/call
  ErpMcp.Gateway    Tools, RBAC, masking, audit chain, rate limiter, input validation
  ErpMcp.Server     ASP.NET Core host: stdio + HTTP transports, console API, static console
web/                Angular 20 Controller Console (standalone components, Signals, zoneless)
tests/              xUnit tests for protocol, policy, accounting rules and the audit chain
```

## Tools

| Tool | Viewer | Accountant | Controller | What it does |
|---|:-:|:-:|:-:|---|
| `list_customers` | ✅ | ✅ | ✅ | Balances, credit limits and utilisation |
| `get_customer` | ✅ masked | ✅ email | ✅ full | Customer detail and open invoices |
| `list_invoices` | ✅ | ✅ | ✅ | Filter by status, customer, days overdue |
| `get_ar_aging` | ✅ | ✅ | ✅ | Receivables aging buckets (current, 1–30 … 90+) |
| `draft_journal_entry` | | ✅ | ✅ | Validated, balanced draft that **waits for human approval** |
| `list_pending_approvals` | | | ✅ | Drafts waiting for a decision |
| `get_audit_log` | | | ✅ | Recent calls and chain verification |

There is deliberately **no MCP tool that posts to the ledger**. Approval only exists in the human console.

## Try it

### 1. Online demo (no install)
Open the live console, go to **Agent playground**, and run the scenarios: overdue invoices, masked bank data, privilege escalation, a write-off draft and prompt injection. Then approve the draft and watch the audit trail.

> Runs on a free instance that sleeps when idle; the first load can take ~30–60 s.

### 2. Run locally with Docker
```bash
docker build -t erp-mcp-gateway .
docker run -p 8080:8080 erp-mcp-gateway
# open http://localhost:8080
```

### 3. Connect Claude Desktop, Cursor or VS Code (stdio)
```bash
git clone https://github.com/yasirmalik36/erp-mcp-gateway && cd erp-mcp-gateway
dotnet build -c Release
```
```json
{
  "mcpServers": {
    "erp": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/erp-mcp-gateway/src/ErpMcp.Server", "--", "--stdio"],
      "env": { "ERP_MCP_ROLE": "Accountant", "ERP_MCP_USER": "you@company.example" }
    }
  }
}
```
Or open it in the official inspector:
```bash
npx @modelcontextprotocol/inspector dotnet run --project src/ErpMcp.Server -- --stdio
```

Then ask your assistant:
- *"Which invoices are more than 60 days overdue, and who owes the most?"*
- *"Give me an AR aging summary I can paste into an email."*
- *"Draft a journal entry to write off INV-1007 against bad debt expense."*
- *"Show me Acme's bank details."* (try it as `Viewer`, then as `Controller`)

### 4. Remote HTTP
```bash
curl -X POST http://localhost:8080/mcp \
  -H "Authorization: Bearer demo-viewer-token" -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```
Demo tokens (`demo-viewer-token`, `demo-accountant-token`, `demo-controller-token`) exist only in `appsettings.Demo.json` and only reach fictional data. In production, map tokens from your identity provider through environment variables and keep `DemoMode` off.

## Development

```bash
# backend
dotnet run --project src/ErpMcp.Server --environment Demo --urls http://localhost:5077
dotnet test

# console (proxies /api and /mcp to :5077)
cd web && npm ci && npx ng serve
npm run build:server   # build the console into the server's wwwroot
```

## Screenshots

| Overview | Approvals |
|---|---|
| ![Overview](docs/overview.png) | ![Approvals](docs/approvals.png) |
| **Agent playground** | **Audit trail** |
| ![Playground](docs/playground.png) | ![Audit trail](docs/audit.png) |

## Design notes

- **Why hand-written MCP?** The protocol surface a tool server needs is small (JSON-RPC, `initialize`, `tools/list`, `tools/call`). Owning it keeps the security-critical path dependency-free and easy to audit. Swapping in the official C# SDK later only touches the `Protocol` project.
- **Tool errors are results, not protocol errors.** Following the MCP spec, a denied or invalid call returns `isError: true` with a clear message, so the model can recover and explain it to the user.
- **The audit chain is evidence, not storage.** It's in-memory here; in production, append each record to a WORM store or a ledger table and verify the chain on read.
- **Roles map to a real finance org:** viewer (sales/ops), accountant (prepares entries), controller (reviews and approves).

## Roadmap
- OAuth 2.1 / OIDC for the HTTP transport (MCP authorization spec)
- Pluggable connectors: Odoo (XML-RPC/JSON-RPC), SAP Business One, Dynamics 365
- Persistent audit store with periodic chain anchoring
- Approval notifications (email / Teams) and four-eyes thresholds by amount

---

All companies, IBANs and invoices are fictional. Built by **[Yasir Mehmood](https://yasir-portfolio-omega.vercel.app)** · [LinkedIn](https://www.linkedin.com/in/yasir-mehmood-53549a1b3) · MIT License.
