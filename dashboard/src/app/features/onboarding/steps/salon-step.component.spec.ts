import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';
import { AuthClient, OnboardingClient, OnboardingStateDto } from '@core/api/api-client';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { OnboardingSalonStepComponent } from './salon-step.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

/**
 * Link podpowiadany z nazwy salonu. To jedno z niewielu pól, przy których człowiek zamiera
 * („co mam wpisać?") — wcześniej trzeba go było wymyślić i wpisać ręcznie.
 *
 * Transliteracja MUSI być po stronie frontu: walidator backendu wymaga `^[a-zA-Z0-9-]+$`,
 * a `Guard.ReplaceSpaces` zamienia tylko spacje na myślniki i polskich znaków nie rusza.
 */
describe('OnboardingSalonStepComponent — link podpowiadany z nazwy', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const setup = (state: unknown = null) => {
    TestBed.configureTestingModule({
      imports: [OnboardingSalonStepComponent],
      providers: [
        OnboardingWizardStore,
        { provide: OnboardingClient, useValue: { completeProfile: vi.fn().mockReturnValue(of({})) } },
        {
          provide: AuthClient,
          useValue: {
            getRegisterOwnerSlugAvailability: vi.fn().mockReturnValue(of({ available: true })),
          },
        },
        {
          provide: OnboardingStateService,
          useValue: {
            markStale: vi.fn(),
            ensure: vi.fn().mockReturnValue(of(state)),
            // `ownSlug` czyta state() w computed — bez sygnału w mocku leci
            // „state is not a function" jako nieprzechwycony wyjątek z subskrypcji.
            state: signal(state as OnboardingStateDto | null),
          },
        },
        { provide: Router, useValue: { navigate: vi.fn() } },
      ],
    });

    const fixture = TestBed.createComponent(OnboardingSalonStepComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  };

  const typeName = (component: OnboardingSalonStepComponent, value: string) =>
    component['onNameInput']({ target: { value } } as unknown as Event);

  const typeSlug = (component: OnboardingSalonStepComponent, value: string) =>
    component['onSlugInput']({ target: { value } } as unknown as Event);

  it('podpowiada link z nazwy', () => {
    const { component } = setup();
    typeName(component, 'Studio Anna Nowak');
    expect(component['salonSlug']()).toBe('studio-anna-nowak');
  });

  it('zdejmuje polskie znaki diakrytyczne', () => {
    const { component } = setup();
    typeName(component, 'Piękność Ćma Żuk');
    expect(component['salonSlug']()).toBe('pieknosc-cma-zuk');
  });

  it('radzi sobie z „ł" — jedyną polską literą, która NIE rozkłada się w NFD', () => {
    const { component } = setup();
    typeName(component, 'Studio Łucja');
    // Bez osobnego przypadku „ł" przeszłoby przez zdejmowanie diakrytyków i wypadło jako myślnik
    // („studio--ucja"), a backend odrzuciłby taki slug.
    expect(component['salonSlug']()).toBe('studio-lucja');
  });

  it('nie zostawia myślników na brzegach ani zdublowanych', () => {
    const { component } = setup();
    typeName(component, '  Salon !!! Uroda  ');
    expect(component['salonSlug']()).toBe('salon-uroda');
  });

  it('wynik zawsze przechodzi wzorzec wymagany przez backend', () => {
    const { component } = setup();
    typeName(component, 'Śliczna Paznokietka & Rzęsy 2026!');
    expect(component['salonSlug']()).toMatch(/^[a-zA-Z0-9-]+$/);
  });

  it('po ręcznej edycji linku przestaje go nadpisywać z nazwy', () => {
    const { component } = setup();

    typeName(component, 'Studio Anna');
    typeSlug(component, 'moj-wlasny-link');
    typeName(component, 'Studio Anna Nowak');

    expect(component['salonSlug']()).toBe('moj-wlasny-link');
  });

  it('istniejący salon: poprawienie nazwy NIE zmienia zapisanego linku', () => {
    // Publiczny adres jest już u klientek — nie wolno go cicho podmienić.
    const { component } = setup({ hasTenant: true, slug: 'ustalony-link', salonName: 'Stara Nazwa' });

    typeName(component, 'Zupełnie Inna Nazwa');

    expect(component['salonSlug']()).toBe('ustalony-link');
  });
});
