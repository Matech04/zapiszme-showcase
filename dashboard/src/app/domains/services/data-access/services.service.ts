import { Injectable, inject } from "@angular/core";
import { ServicesClient, ServiceDto as GeneratedServiceDto, Money } from "@core/api/api-client";
import { Observable, map } from "rxjs";
import { BaseApiService } from "@core/api/base-api.service";

// Definiujemy lokalny interfejs, który jest zgodny z tym czego oczekuje BaseCrudComponent,
// ale mapuje pola z DTO wygenerowanego przez NSwag.
export interface ServiceDto {
  id: string;
  name: string;
  category: string;
  price: Money;
  duration: number;
}

@Injectable({ providedIn: 'root' })
export class ServicesService extends BaseApiService<ServiceDto> {
  protected override endpoint = "services";
  private client = inject(ServicesClient);

  override getAll(): Observable<ServiceDto[]> {
    return this.client.getServices().pipe(
      map(dtos => dtos.map(dto => this.mapToLocal(dto)))
    );
  }

  override getById(id: string): Observable<ServiceDto> {
    return this.client.getService(id).pipe(
      map(dto => this.mapToLocal(dto))
    );
  }

  override create(item: any): Observable<any> {
    return this.client.createService(item);
  }

  override update(id: string, item: any): Observable<any> {
    return this.client.updateService(id, item);
  }

  override delete(id: string): Observable<any> {
    return this.client.deleteService(id);
  }

  private mapToLocal(dto: GeneratedServiceDto): ServiceDto {
    return {
      id: dto.id || '',
      name: dto.name || '',
      category: dto.categoryId || '',
      price: dto.price || { amount: 0, currency: 'PLN' },
      duration: dto.durationInMinutes || 0
    };
  }
}
