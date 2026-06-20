import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService, Endpoint } from './services/api.service';

interface RateLimitInfo {
  limit: number;
  remaining: number;
  resetEpochSec: number;
  retryAfter?: number;
}

interface RequestLog {
  id: number;
  timestamp: Date;
  method: string;
  endpoint: string;
  status: number;
  remaining: number | null;
  limit: number | null;
  retryAfter: number | null;
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly endpoints: Endpoint[] = [
    { method: 'GET',    path: '/api/data',   label: 'GET /api/data — read' },
    { method: 'POST',   path: '/api/data',   label: 'POST /api/data — write' },
    { method: 'PUT',    path: '/api/data/1', label: 'PUT /api/data/1 — write' },
    { method: 'DELETE', path: '/api/data/1', label: 'DELETE /api/data/1 — write' },
  ];

  readonly tiers = ['standard', 'premium', 'enterprise'];

  clientId    = signal('client-001');
  clientTier  = signal('standard');
  selected    = signal<Endpoint>(this.endpoints[0]);
  isLoading   = signal(false);
  isBursting  = signal(false);

  lastStatus   = signal<number | null>(null);
  lastBody     = signal<unknown>(null);
  rateLimitInfo = signal<RateLimitInfo | null>(null);
  logs          = signal<RequestLog[]>([]);

  private logId = 0;

  constructor(private api: ApiService) {}

  sendRequest(): void {
    if (this.isLoading()) return;
    this.isLoading.set(true);
    const { method, path } = this.selected();

    this.api.request(method, path, this.clientId(), this.clientTier()).subscribe({
      next:  resp  => { this.applyResponse(resp.status, resp.body, resp.headers); this.isLoading.set(false); },
      error: (err: HttpErrorResponse) => { this.applyResponse(err.status, err.error, err.headers); this.isLoading.set(false); },
    });
  }

  burstTest(): void {
    this.isBursting.set(true);
    let sent = 0;
    const total = 15;
    const interval = setInterval(() => {
      this.sendRequest();
      if (++sent >= total) { clearInterval(interval); this.isBursting.set(false); }
    }, 80);
  }

  clearLogs(): void { this.logs.set([]); }

  usagePercent(): number {
    const info = this.rateLimitInfo();
    if (!info || info.limit === 0) return 0;
    return ((info.limit - info.remaining) / info.limit) * 100;
  }

  resetTime(): string {
    const info = this.rateLimitInfo();
    if (!info) return '';
    return new Date(info.resetEpochSec * 1000).toLocaleTimeString();
  }

  private applyResponse(status: number, body: unknown, headers: any): void {
    this.lastStatus.set(status);
    this.lastBody.set(body);

    const limit       = parseInt(headers.get('X-RateLimit-Limit') ?? '0', 10);
    const remaining   = parseInt(headers.get('X-RateLimit-Remaining') ?? '0', 10);
    const resetEpoch  = parseInt(headers.get('X-RateLimit-Reset') ?? '0', 10);
    const retryAfter  = status === 429 ? parseInt(headers.get('Retry-After') ?? '0', 10) : undefined;

    this.rateLimitInfo.set({ limit, remaining, resetEpochSec: resetEpoch, retryAfter });

    const { method, path } = this.selected();
    this.logs.update(prev => [
      {
        id: ++this.logId,
        timestamp: new Date(),
        method,
        endpoint: path,
        status,
        remaining: isNaN(remaining) ? null : remaining,
        limit: isNaN(limit) ? null : limit,
        retryAfter: retryAfter ?? null,
      },
      ...prev.slice(0, 29),
    ]);
  }
}
