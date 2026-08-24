/** Produkcyjne źródło danych — opakowuje NSwag `BookingApiClient` i wiąże slug salonu. */
import { createBookingApiClient } from "../booking-api-browser";
import type {
  BookingDataSource,
  ConfirmWithSessionInput,
  HoldRequest,
  RequestOtpInput,
  SalonBundle,
  VerifyOtpInput,
} from "./data-source";
import type { ServiceCategoryDto } from "../booking-openapi-client";

export function apiBookingDataSource(salonSlug: string): BookingDataSource {
  return {
    salonSlug,

    async loadSalon(signal): Promise<SalonBundle> {
      const client = createBookingApiClient(signal);
      const [services, serviceCategories, salonInfo] = await Promise.all([
        client.bookingServices_GetServices(salonSlug),
        client
          .bookingServiceCategories_GetServiceCategories(salonSlug)
          .catch(() => [] as ServiceCategoryDto[]),
        client.publicBookingSalon_Get(salonSlug),
      ]);
      return {
        services: services ?? [],
        serviceCategories: serviceCategories ?? [],
        salonInfo: salonInfo ?? null,
      };
    },

    async loadEmployees(serviceIds, signal) {
      const client = createBookingApiClient(signal);
      // Pusta lista → wszyscy bookowalni pracownicy (start lejka: pracownik przed usługą).
      // Niepusta → intersekcja: pracownicy oferujący WSZYSTKIE wybrane usługi (combo realizuje jeden).
      return (
        (await client.bookingEmployees_GetEmployees(salonSlug, serviceIds)) ?? []
      );
    },

    async loadServices(employeeId, signal) {
      const client = createBookingApiClient(signal);
      // employeeId → backend resolvuje cenę/czas per-pracownik (CustomPrice/CustomDuration).
      return (
        (await client.bookingServices_GetServices(salonSlug, undefined, employeeId)) ?? []
      );
    },

    async loadMonthAvailability(year, month, employeeId, serviceIds, signal) {
      const client = createBookingApiClient(signal);
      return (
        (await client.bookingAppointments_GetMonthAvailability(
          salonSlug,
          year,
          month,
          employeeId,
          serviceIds,
        )) ?? { isClosed: false, opensOn: undefined, days: [] }
      );
    },

    async loadSlots(date, employeeId, serviceIds, signal) {
      const client = createBookingApiClient(signal);
      return (
        (await client.bookingAppointments_GetAvailableSlots(
          salonSlug,
          date,
          employeeId,
          serviceIds,
        )) ?? []
      );
    },

    async createHold(body: HoldRequest, signal) {
      const client = createBookingApiClient(signal);
      return client.publicCreateAppointment_PublicCreateHold(salonSlug, {
        serviceIds: body.serviceIds,
        employeeId: body.employeeId,
        date: body.date,
        startTime: body.startTime,
        turnstileToken: body.turnstileToken,
      });
    },

    async attachInspiration(appointmentId, uploadToken, file: File, signal) {
      const client = createBookingApiClient(signal);
      const result = await client.bookingInspirations_AttachInspiration(
        salonSlug,
        appointmentId,
        uploadToken,
        { data: file, fileName: file.name },
      );
      return {
        url: result?.url ?? "",
        thumbnailUrl: result?.thumbnailUrl ?? "",
        key: result?.key ?? "",
      };
    },

    async updateHold(appointmentId, body, signal) {
      const client = createBookingApiClient(signal);
      return client.publicCreateAppointment_PublicUpdateAppointment(
        salonSlug,
        {
          token: body.token,
          serviceIds: body.serviceIds,
          employeeId: body.employeeId,
          date: body.date,
          startTime: body.startTime,
          turnstileToken: body.turnstileToken,
        },
        appointmentId,
      );
    },

    async requestOtp(appointmentId, body: RequestOtpInput) {
      const client = createBookingApiClient();
      await client.publicOtp_RequestOtp(salonSlug, appointmentId, body);
    },

    async verifyOtp(appointmentId, body: VerifyOtpInput) {
      const client = createBookingApiClient();
      const result = await client.publicOtp_VerifyOtp(salonSlug, appointmentId, body);
      return {
        requiresManualConfirmation: result?.requiresManualConfirmation ?? false,
        sessionToken: result?.sessionToken,
        sessionExpiresAtUtc: result?.sessionExpiresAtUtc as unknown as string | undefined,
        inspirationUploadToken: result?.inspirationUploadToken,
      };
    },

    async confirmWithSession(appointmentId, body: ConfirmWithSessionInput) {
      const client = createBookingApiClient();
      const result = await client.publicOtp_ConfirmWithSession(salonSlug, appointmentId, body);
      return {
        requiresManualConfirmation: result?.requiresManualConfirmation ?? false,
        inspirationUploadToken: result?.inspirationUploadToken,
      };
    },
  };
}
