import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Api, AuditPage } from './api';

@Component({
  selector: 'app-audit-trail',
  imports: [DatePipe],
  template: `
    <section class="card">
      <div class="row">
        <div>
          <h2>Audit trail</h2>
          <p class="muted">Every agent call and human decision, newest first. Each record's SHA-256 hash covers the previous one, so any edit breaks the chain.</p>
        </div>
        <div class="row">
        @if (page(); as p) {
          <button type="button" (click)="exportCsv()" [disabled]="!p.records.length">Export CSV</button>
          <span class="pill" [class.good]="p.chainVerified" [class.bad]="!p.chainVerified">
            <span class="dot"></span>{{ p.chainVerified ? 'Chain verified' : 'Chain broken' }} · {{ p.total }} records
          </span>
        }
        </div>
      </div>
      <div class="table-wrap">
        <table class="audit">
          <thead>
            <tr><th>#</th><th>Time (UTC)</th><th>Caller</th><th>Role</th><th>Tool</th><th>Outcome</th><th>Hash</th></tr>
          </thead>
          <tbody>
            @for (r of page()?.records ?? []; track r.sequence) {
              <tr>
                <td class="mono">{{ r.sequence }}</td>
                <td>{{ r.at | date:'HH:mm:ss':'UTC' }}</td>
                <td>{{ r.callerId }}</td>
                <td><span class="chip role">{{ r.role }}</span></td>
                <td class="mono" [title]="r.arguments">{{ r.tool }}</td>
                <td><span class="chip" [class.ok]="r.outcome === 'ok'" [class.deny]="r.outcome !== 'ok'">{{ r.outcome }}</span></td>
                <td class="mono muted" [title]="r.hash">{{ r.hash.slice(0, 10) }}…</td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="muted">No calls yet. Run something in the Agent playground.</td></tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class AuditTrail {
  private readonly api = inject(Api);
  protected readonly page = signal<AuditPage | null>(null);

  constructor() {
    this.load();
    const t = setInterval(() => this.load(), 3000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected exportCsv(): void {
    const records = this.page()?.records ?? [];
    const cols = ['sequence', 'at', 'callerId', 'tool', 'outcome', 'previousHash', 'hash'] as const;
    const esc = (v: unknown) => {
      const t = String(v ?? '');
      return /[",\r\n]/.test(t) ? `"${t.replace(/"/g, '""')}"` : t;
    };
    const lines = [cols.join(','), ...records.map(r => cols.map(c => esc(r[c])).join(','))];
    const blob = new Blob(['﻿' + lines.join('\r\n') + '\r\n'], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'audit-trail.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  private load(): void { this.api.audit(100).subscribe(p => this.page.set(p)); }
}
