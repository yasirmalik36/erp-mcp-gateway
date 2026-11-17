import { Component, computed, input, output } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Summary } from './api';

@Component({
  selector: 'app-overview',
  imports: [CurrencyPipe, DecimalPipe],
  template: `
    <section class="intro card">
      <h2>AI agents get answers. Humans keep control.</h2>
      <p>
        This gateway lets AI assistants (Claude, Copilot, Cursor) query a sample accounting system through the
        <b>Model Context Protocol</b>, under the same guardrails you'd expect from a finance system:
        role-based tools, masked bank data, a human approval step for every write, and a tamper-evident audit log.
      </p>
      <div class="cta">
        <button class="primary" (click)="go.emit('playground')">Try it as an agent →</button>
        <button (click)="go.emit('approvals')">Review pending approvals</button>
      </div>
    </section>

    @if (summary(); as s) {
      <section class="kpis">
        <div class="kpi card"><span>Outstanding receivables</span><b>{{ s.aging.totalOutstanding | currency:'USD':'symbol':'1.0-0' }}</b></div>
        <div class="kpi card"><span>Pending human approvals</span><b>{{ s.pendingApprovals }}</b></div>
        <div class="kpi card"><span>Agent tool calls</span><b>{{ s.toolCalls | number }}</b></div>
        <div class="kpi card"><span>Calls denied by policy</span><b class="warn">{{ s.denied | number }}</b></div>
      </section>

      <section class="grid2">
        <div class="card">
          <h3>Receivables aging <small>as of {{ s.aging.asOf }}</small></h3>
          @for (b of s.aging.buckets; track b.bucket) {
            <div class="bar-row">
              <span class="lbl">{{ b.bucket }}</span>
              <div class="track"><div class="fill" [class.late]="b.bucket === '90+'" [style.width.%]="pct(b.amount)"></div></div>
              <span class="val">{{ b.amount | currency:'USD':'symbol':'1.0-0' }} · {{ b.invoices }}</span>
            </div>
          }
        </div>
        <div class="card">
          <h3>Agent calls by role</h3>
          @for (r of roles; track r) {
            <div class="bar-row">
              <span class="lbl">{{ r }}</span>
              <div class="track"><div class="fill role" [style.width.%]="rolePct(r)"></div></div>
              <span class="val">{{ count(r) }}</span>
            </div>
          }
          <p class="muted">Try the playground: calls appear here and in the audit trail within seconds.</p>
        </div>
      </section>
    }

    <section class="card guardrails">
      <h3>Guardrails built in</h3>
      <ul>
        <li><b>Least privilege.</b> Viewers, Accountants and Controllers each see different tools, and the server re-checks on every call.</li>
        <li><b>Data masking.</b> IBANs and emails are masked unless the role needs them.</li>
        <li><b>Human-in-the-loop.</b> Agents can only <i>draft</i> journal entries. A person approves them here, and nobody can approve their own draft.</li>
        <li><b>Tamper-evident audit.</b> Every call is hash-chained (SHA-256); editing any record breaks the chain.</li>
        <li><b>Prompt-injection defence.</b> ERP records are returned as labelled data, never as instructions. Invoice INV-1007 contains a planted attack to prove it.</li>
        <li><b>Abuse limits.</b> Rate limiting, input validation, payload caps and Origin checks on HTTP.</li>
      </ul>
    </section>
  `,
})
export class Overview {
  readonly summary = input<Summary | null>(null);
  readonly go = output<'playground' | 'approvals'>();
  protected readonly roles = ['Viewer', 'Accountant', 'Controller'];

  private readonly maxBucket = computed(() => Math.max(1, ...(this.summary()?.aging.buckets.map(b => b.amount) ?? [1])));
  private readonly maxRole = computed(() => Math.max(1, ...Object.values(this.summary()?.callsByRole ?? {})));

  protected pct(amount: number): number { return (amount / this.maxBucket()) * 100; }
  protected count(role: string): number { return this.summary()?.callsByRole[role] || 0; }
  protected rolePct(role: string): number { return ((this.summary()?.callsByRole[role] ?? 0) / this.maxRole()) * 100; }
}
