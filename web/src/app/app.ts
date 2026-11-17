import { Component, DestroyRef, inject, signal } from '@angular/core';
import { Api, Summary } from './api';
import { Overview } from './overview';
import { Approvals } from './approvals';
import { AuditTrail } from './audit-trail';
import { Playground } from './playground';
import { Connect } from './connect';

type Tab = 'overview' | 'approvals' | 'audit' | 'playground' | 'connect';

@Component({
  selector: 'app-root',
  imports: [Overview, Approvals, AuditTrail, Playground, Connect],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly api = inject(Api);
  protected readonly tab = signal<Tab>('overview');
  protected readonly summary = signal<Summary | null>(null);
  protected readonly offline = signal(false);
  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'approvals', label: 'Approvals' },
    { id: 'audit', label: 'Audit trail' },
    { id: 'playground', label: 'Agent playground' },
    { id: 'connect', label: 'Connect your AI' },
  ];

  constructor() {
    this.refresh();
    // Live console: poll the gateway so agent activity appears without reloading.
    const timer = setInterval(() => this.refresh(), 4000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  refresh(): void {
    this.api.summary().subscribe({
      next: s => { this.summary.set(s); this.offline.set(false); },
      error: () => this.offline.set(true),
    });
  }
}
