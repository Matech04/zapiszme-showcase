import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ServiceDto } from '@core/api/api-client';
import { ServiceCardComponent } from './service-card.component';

function makeService(partial: Partial<ServiceDto>): ServiceDto {
  return {
    id: 's1',
    name: 'Usługa',
    price: { amount: 100, currency: 'PLN' },
    durationInMinutes: 60,
    ...partial,
  } as ServiceDto;
}

describe('ServiceCardComponent — cena bezpłatna / ukryta', () => {
  let fixture: ComponentFixture<ServiceCardComponent>;
  let component: ServiceCardComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ServiceCardComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(ServiceCardComponent);
    component = fixture.componentInstance;
  });

  function render(service: ServiceDto) {
    fixture.componentRef.setInput('service', service);
    fixture.detectChanges();
  }

  it('pokazuje „Bezpłatnie" gdy cena = 0', () => {
    render(makeService({ price: { amount: 0, currency: 'PLN' } }));
    const free = fixture.nativeElement.querySelector('[data-testid="service-card-price-free"]');
    expect(free).toBeTruthy();
    expect(free.textContent.trim()).toBe('Bezpłatnie');
    expect((component as any).isFree()).toBe(true);
  });

  it('NIE pokazuje „Bezpłatnie" gdy cena 0 ma widełki (od–do)', () => {
    render(makeService({ price: { amount: 0, currency: 'PLN' }, maxAmount: 50 }));
    expect(fixture.nativeElement.querySelector('[data-testid="service-card-price-free"]')).toBeNull();
    expect((component as any).isFree()).toBe(false);
  });

  it('pokazuje „Cena ukryta" gdy hidePrice = true (priorytet nad kwotą)', () => {
    render(makeService({ price: { amount: 120, currency: 'PLN' }, hidePrice: true }));
    const hidden = fixture.nativeElement.querySelector('[data-testid="service-card-price-hidden"]');
    expect(hidden).toBeTruthy();
    expect(hidden.textContent).toContain('Cena ukryta');
    expect(fixture.nativeElement.querySelector('[data-testid="service-card-price"]')).toBeNull();
  });

  it('pokazuje kwotę gdy cena > 0 i hidePrice = false', () => {
    render(makeService({ price: { amount: 120, currency: 'PLN' }, hidePrice: false }));
    const price = fixture.nativeElement.querySelector('[data-testid="service-card-price"]');
    expect(price).toBeTruthy();
    expect(price.className).toContain('break-words');
  });
});
