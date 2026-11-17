import { Component, signal } from '@angular/core';

@Component({
  selector: 'app-connect',
  template: `
    <section class="card">
      <h2>Connect a real AI client</h2>
      <p class="muted">Run the gateway locally over stdio. The role you set is the role the agent gets.</p>

      <h3>1 · Run with Docker or .NET</h3>
      <pre class="code">{{ run }}</pre>

      <h3>2 · Claude Desktop / Cursor (<span class="mono">mcpServers</span> config)</h3>
      <pre class="code">{{ desktop }}</pre>

      <h3>3 · Or inspect it with the official MCP Inspector</h3>
      <pre class="code">{{ inspector }}</pre>

      <h3>Try asking</h3>
      <ul class="prompts">
        @for (p of prompts; track p) { <li>“{{ p }}”</li> }
      </ul>

      <h3>Remote HTTP endpoint</h3>
      <p class="muted">
        <span class="mono">POST {{ origin() }}/mcp</span> with <span class="mono">Authorization: Bearer &lt;token&gt;</span>.
        The demo tokens are <span class="mono">demo-viewer-token</span>, <span class="mono">demo-accountant-token</span> and
        <span class="mono">demo-controller-token</span>. They're fictional data only; in production, tokens come from your identity provider.
      </p>
    </section>
  `,
})
export class Connect {
  protected readonly origin = signal(typeof location !== 'undefined' ? location.origin : '');
  protected readonly run =
`git clone https://github.com/yasirmalik36/erp-mcp-gateway
cd erp-mcp-gateway
dotnet run --project src/ErpMcp.Server -- --stdio`;
  protected readonly desktop =
`{
  "mcpServers": {
    "erp": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/erp-mcp-gateway/src/ErpMcp.Server", "--", "--stdio"],
      "env": { "ERP_MCP_ROLE": "Accountant", "ERP_MCP_USER": "you@company.example" }
    }
  }
}`;
  protected readonly inspector =
`npx @modelcontextprotocol/inspector dotnet run --project src/ErpMcp.Server -- --stdio`;
  protected readonly prompts = [
    'Which invoices are more than 60 days overdue, and who owes the most?',
    'Give me an AR aging summary I can paste into an email.',
    'Draft a journal entry to write off INV-1007 against bad debt expense.',
    'Show me Acme’s bank details.  (try it as Viewer, then as Controller)',
  ];
}
