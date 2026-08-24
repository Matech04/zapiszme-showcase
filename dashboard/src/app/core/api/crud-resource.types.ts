import { Observable } from 'rxjs';
import { ResourceRef } from '@angular/core';

export interface CrudClient<T, TCreate, TUpdate> {
  get(id: string): Observable<T>;
  create(data: TCreate): Observable<string | any>;
  update(id: string, data: TUpdate): Observable<any>;
}

export interface FormResourceResult<T> {
  resource: ResourceRef<T | undefined>;
  save: (data: any) => void;
  isLoading: () => boolean;
  error: () => string | null;
}
