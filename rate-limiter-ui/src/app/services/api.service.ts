import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Endpoint {
  method: string;
  path: string;
  label: string;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  readonly baseUrl = 'https://localhost:44339';

  constructor(private http: HttpClient) {}

  request(
    method: string,
    path: string,
    clientId: string,
    clientTier: string
  ): Observable<HttpResponse<unknown>> {
    const headers = new HttpHeaders({
      'X-Client-Id': clientId,
      'X-Client-Tier': clientTier,
      'Content-Type': 'application/json',
    });

    const url = `${this.baseUrl}${path}`;
    const opts = { headers, observe: 'response' as const };

    switch (method) {
      case 'POST':   return this.http.post<unknown>(url, {}, opts);
      case 'PUT':    return this.http.put<unknown>(url, {}, opts);
      case 'DELETE': return this.http.delete<unknown>(url, opts);
      default:       return this.http.get<unknown>(url, opts);
    }
  }
}
