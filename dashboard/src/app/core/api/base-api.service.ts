import { HttpClient } from "@angular/common/http"
import { inject } from "@angular/core"
import { Observable } from "rxjs";

export abstract class BaseApiService<T> {

  protected http = inject(HttpClient);

  protected abstract endpoint: string;

  protected apiUrl = 'http://localhost:5001/api'

  get url() {
    return `${this.apiUrl}/${this.endpoint}`
  }

  getAll(): Observable<T[]> {
    return this.http.get<T[]>(this.url);
  }

  getById(id: string): Observable<T> {
    return this.http.get<T>(`${this.url}/${id}`)
  }

  create(item: T): Observable<T> {
    return this.http.post<T>(this.url, item)
  }

  update(id: string, item: Partial<T>): Observable<T> {
    return this.http.put<T>(`${this.url}/${id}`, item);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}
