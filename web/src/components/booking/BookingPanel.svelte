<script lang="ts">
  import BookingFlow from "./BookingFlow.svelte";
  import { apiBookingDataSource } from "../../lib/booking/api-data-source";

  let {
    salonSlug,
    onsalonNotFound,
  }: { salonSlug: string; onsalonNotFound?: () => void } = $props();

  // Remount całego flow przy zmianie salonu (świeży stan, hold-storage per slug).
  const dataSource = $derived(apiBookingDataSource(salonSlug));
</script>

{#key salonSlug}
  <BookingFlow
    {dataSource}
    {salonSlug}
    enableBotCheck
    persistHold
    setDocumentTitle
    layout="wizard"
    datePickerVariant="responsive"
    {onsalonNotFound}
  />
{/key}
