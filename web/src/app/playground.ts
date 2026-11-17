import { Component, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Role, ToolInfo } from './api';

interface Scenario { title: string; hint: string; role: Role; tool: string; args: object; }

@Component({
  selector: 'app-playground',
  imports: [FormsModule],
  template: `
    <section class="card">
      <h2>Agent playground</h2>
      <p class="muted">
        Act as an AI agent without installing anything. Pick a role and a tool, and the request goes through the same MCP
        endpoint and guardrails a real client uses. Watch the result, then check the audit trail.
      </p>
      <div class="scenarios">
        @for (s of scenarios; track s.title) {
          <button class="scenario" (click)="load(s)"><b>{{ s.title }}</b><span>{{ s.hint }}</span></button>
        }
      </div>
    </section>

    <section class="grid2">
      <div class="card">
        <label>Agent role
          <select [ngModel]="role()" (ngModelChange)="setRole($event)">
            <option>Viewer</option><option>Accountant</option><option>Controller</option>
          </select>
        </label>
        <label>Tool <small class="muted">(only tools this role is allowed to see)</small>
          <select [ngModel]="tool()" (ngModelChange)="tool.set($event)">
            @for (t of tools(); track t.name) { <option [value]="t.name">{{ t.name }}</option> }
          </select>
        </label>
        @if (current(); as t) { <p class="muted small">{{ t.description }}</p> }
        <label>Arguments (JSON)
          <textarea rows="9" spellcheck="false" [ngModel]="args()" (ngModelChange)="args.set($event)"></textarea>
        </label>
        <button class="primary" (click)="run()" [disabled]="running()">{{ running() ? 'Calling…' : 'Call tool' }}</button>
      </div>
      <div class="card">
        <h3>MCP response
          @if (isError() !== null) {
            <span class="chip" [class.ok]="!isError()" [class.deny]="isError()">{{ isError() ? 'isError: true' : 'ok' }}</span>
          }
        </h3>
        <pre class="json">{{ readable() || 'Run a call to see the result here.' }}</pre>
        @if (output()) {
          <details><summary class="muted small">Raw JSON-RPC response</summary><pre class="json">{{ output() }}</pre></details>
        }
      </div>
    </section>
  `,
})
export class Playground {
  private readonly api = inject(Api);
  readonly called = output<void>();

  protected readonly role = signal<Role>('Viewer');
  protected readonly tools = signal<ToolInfo[]>([]);
  protected readonly tool = signal('list_invoices');
  protected readonly args = signal('{\n  "minDaysOverdue": 90\n}');
  protected readonly output = signal('');
  protected readonly readable = signal('');
  protected readonly isError = signal<boolean | null>(null);
  protected readonly running = signal(false);

  protected readonly scenarios: Scenario[] = [
    { title: 'Overdue invoices', hint: 'Viewer asks what is 90+ days late', role: 'Viewer', tool: 'list_invoices', args: { minDaysOverdue: 90 } },
    { title: 'Masked bank data', hint: 'Viewer looks up a customer', role: 'Viewer', tool: 'get_customer', args: { customerId: 'ACME' } },
    { title: 'Privilege escalation', hint: 'Viewer tries to draft an entry', role: 'Viewer', tool: 'draft_journal_entry',
      args: { description: 'Sneaky', lines: [{ account: '1000', debit: 99999 }, { account: '4000', credit: 99999 }] } },
    { title: 'Draft a write-off', hint: 'Accountant drafts; a human approves', role: 'Accountant', tool: 'draft_journal_entry',
      args: { description: 'Write off INV-1007 (Zephyr Health, 160 days overdue)', lines: [{ account: '6100', debit: 4250 }, { account: '1100', credit: 4250 }] } },
    { title: 'Unbalanced entry', hint: 'Validation catches bad accounting', role: 'Accountant', tool: 'draft_journal_entry',
      args: { description: 'Typo', lines: [{ account: '6200', debit: 25 }, { account: '1000', credit: 20 }] } },
    { title: 'Prompt injection', hint: 'INV-1007 memo carries an attack', role: 'Viewer', tool: 'get_customer', args: { customerId: 'ZEPH' } },
  ];

  constructor() { this.loadTools(); }

  protected current(): ToolInfo | undefined { return this.tools().find(t => t.name === this.tool()); }

  protected setRole(r: Role): void { this.role.set(r); this.loadTools(); }

  private loadTools(keep?: string): void {
    this.api.mcp(this.role(), 'tools/list').subscribe(res => {
      const list: ToolInfo[] = res.result?.tools ?? [];
      this.tools.set(list);
      if (keep) return;
      if (!list.some(t => t.name === this.tool())) this.tool.set(list[0]?.name ?? '');
    });
  }

  protected load(s: Scenario): void {
    this.role.set(s.role);
    this.tool.set(s.tool);
    this.args.set(JSON.stringify(s.args, null, 2));
    this.loadTools(s.tool);
    this.run();
  }

  protected run(): void {
    let parsed: unknown;
    try { parsed = JSON.parse(this.args() || '{}'); }
    catch { this.readable.set('Arguments are not valid JSON.'); this.output.set(''); this.isError.set(true); return; }

    this.running.set(true);
    this.api.mcp(this.role(), 'tools/call', { name: this.tool(), arguments: parsed }).subscribe({
      next: res => {
        this.running.set(false);
        this.isError.set(res.result?.isError ?? !!res.error);
        this.output.set(JSON.stringify(res, null, 2));
        const r = res.result;
        this.readable.set(r?.structuredContent ? JSON.stringify(r.structuredContent, null, 2)
          : r?.content?.[0]?.text ?? res.error?.message ?? '');
        this.called.emit();
      },
      error: err => {
        this.running.set(false);
        this.isError.set(true);
        this.output.set('');
        this.readable.set(err.status === 404 ? 'The playground is only enabled in demo mode.' : 'Request failed.');
      },
    });
  }
}
