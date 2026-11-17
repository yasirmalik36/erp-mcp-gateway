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
  protected readonly theme = signal<'light' | 'dark'>(App.initialTheme());
  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'approvals', label: 'Approvals' },
    { id: 'audit', label: 'Audit trail' },
    { id: 'playground', label: 'Agent playground' },
    { id: 'connect', label: 'Connect your AI' },
  ];

  constructor() {
    this.applyTheme(this.theme());
    this.refresh();
    // Live console: poll the gateway so agent activity appears without reloading.
    const timer = setInterval(() => this.refresh(), 4000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  /** ?theme=light|dark in the URL wins, then the saved choice, then the OS setting. */
  private static initialTheme(): 'light' | 'dark' {
    const fromUrl = new URLSearchParams(location.search).get('theme');
    if (fromUrl === 'light' || fromUrl === 'dark') return fromUrl;
    try {
      const saved = localStorage.getItem('erp-mcp-theme');
      if (saved === 'light' || saved === 'dark') return saved;
    } catch { /* storage unavailable */ }
    return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  private applyTheme(t: 'light' | 'dark'): void {
    document.documentElement.dataset['theme'] = t;
  }

  protected toggleTheme(): void {
    const next = this.theme() === 'light' ? 'dark' : 'light';
    this.theme.set(next);
    this.applyTheme(next);
    try { localStorage.setItem('erp-mcp-theme', next); } catch { /* ignore */ }
  }

  refresh(): void {
    this.api.summary().subscribe({
      next: s => { this.summary.set(s); this.offline.set(false); },
      error: () => this.offline.set(true),
    });
  }
}
