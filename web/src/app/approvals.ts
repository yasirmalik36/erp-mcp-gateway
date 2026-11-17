import { Component, inject, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Api, JournalEntry } from './api';

@Component({
  selector: 'app-approvals',
  imports: [CurrencyPipe, DatePipe],
  template: `
    <section class="card">
      <h2>Approvals inbox</h2>
      <p class="muted">
        Journal entries drafted by AI agents. Nothing reaches the ledger until a human approves it here,
        signed in as <b>controller.human</b>, who can never approve their own drafts (segregation of duties).
      </p>
      @if (error()) { <div class="alert">{{ error() }}</div> }
    </section>

    @for (e of entries(); track e.id) {
      <article class="card entry" [class.done]="e.status !== 'PendingApproval'">
        <header>
          <div>
            <span class="mono">{{ e.id }}</span>
            <span class="chip" [class]="e.status">{{ label(e.status) }}</span>
          </div>
          <b>{{ e.total | currency:'USD' }}</b>
        </header>
        <p class="desc">{{ e.description }}</p>
        <table class="lines">
          <thead><tr><th>Account</th><th class="num">Debit</th><th class="num">Credit</th></tr></thead>
          <tbody>
            @for (l of e.lines; track $index) {
              <tr>
                <td class="mono">{{ l.account }}</td>
                <td class="num">{{ l.debit ? (l.debit | currency:'USD') : '' }}</td>
                <td class="num">{{ l.credit ? (l.credit | currency:'USD') : '' }}</td>
              </tr>
            }
          </tbody>
        </table>
        <footer>
          <span class="muted">
            Drafted by <b>{{ e.draftedBy }}</b> · {{ e.draftedAt | date:'medium' }}
            @if (e.decidedBy) { · {{ e.status === 'Posted' ? 'Approved' : 'Rejected' }} by <b>{{ e.decidedBy }}</b> }
          </span>
          @if (e.status === 'PendingApproval') {
            <span class="actions">
              <button (click)="decide(e, 'reject')" [disabled]="busy()">Reject</button>
              <button class="primary" (click)="decide(e, 'approve')" [disabled]="busy()">Approve &amp; post</button>
            </span>
          }
        </footer>
      </article>
    } @empty {
      <section class="card muted">No drafts yet. Use the Agent playground as an Accountant to draft one.</section>
    }
  `,
})
export class Approvals {
  private readonly api = inject(Api);
  readonly changed = output<void>();
  protected readonly entries = signal<JournalEntry[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  constructor() { this.load(); }

  private load(): void { this.api.approvals().subscribe(e => this.entries.set(e)); }

  protected label(s: JournalEntry['status']): string {
    return s === 'PendingApproval' ? 'Pending approval' : s;
  }

  protected decide(e: JournalEntry, decision: 'approve' | 'reject'): void {
    this.busy.set(true);
    this.error.set('');
    this.api.decide(e.id, decision).subscribe({
      next: () => { this.busy.set(false); this.load(); this.changed.emit(); },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(err.error?.error ?? 'Could not record the decision.');
        this.load();
      },
    });
  }
}
