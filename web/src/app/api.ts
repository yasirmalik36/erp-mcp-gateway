import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type Role = 'Viewer' | 'Accountant' | 'Controller';

export interface AgingBucket { bucket: string; amount: number; invoices: number; }
export interface Summary {
  aging: { asOf: string; currency: string; buckets: AgingBucket[]; totalOutstanding: number };
  pendingApprovals: number;
  toolCalls: number;
  denied: number;
  chainVerified: boolean;
  callsByRole: Record<string, number>;
}

export interface JournalLine { account: string; debit: number; credit: number; }
export interface JournalEntry {
  id: string;
  description: string;
  status: 'PendingApproval' | 'Posted' | 'Rejected';
  draftedBy: string;
  draftedAt: string;
  decidedBy?: string | null;
  decidedAt?: string | null;
  total: number;
  lines: JournalLine[];
}

export interface AuditRecord {
  sequence: number;
  at: string;
  callerId: string;
  role: Role;
  tool: string;
  arguments: string;
  outcome: string;
  previousHash: string;
  hash: string;
}
export interface AuditPage { chainVerified: boolean; total: number; records: AuditRecord[]; }

export interface ToolInfo { name: string; title: string; description: string; inputSchema: unknown; }

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  summary(): Observable<Summary> { return this.http.get<Summary>('/api/summary'); }
  approvals(): Observable<JournalEntry[]> { return this.http.get<JournalEntry[]>('/api/approvals'); }
  decide(id: string, decision: 'approve' | 'reject'): Observable<JournalEntry> {
    return this.http.post<JournalEntry>(`/api/approvals/${encodeURIComponent(id)}/${decision}`, {});
  }
  audit(limit = 50): Observable<AuditPage> { return this.http.get<AuditPage>(`/api/audit?limit=${limit}`); }

  /** Sends one MCP JSON-RPC request as an agent with the given role (demo playground). */
  mcp(role: Role, method: string, params: unknown = {}): Observable<any> {
    return this.http.post<any>('/api/playground', { role, method, params });
  }
}
